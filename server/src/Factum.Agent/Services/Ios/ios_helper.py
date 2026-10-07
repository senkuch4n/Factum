# -*- coding: utf-8 -*-
"""Helper de iPhone de Tatana (SDD ios-herramientas-windows §6.2).

Un solo script multiplataforma (Windows con el Python embebido 3.11 del portátil, macOS con el
Python de Homebrew) que Tatana lleva como recurso embebido y escribe en
%TEMP%/tatana/ios_helper_<sha8>.py. Requiere pymobiledevice3 10.7.4 (y Pillow, dependencia de
pymobiledevice3, solo para --synthetic).

    python ios_helper.py devices
    python ios_helper.py prepare     --udid U
    python ios_helper.py screenshot  --udid U --output P
    python ios_helper.py record      --udid U --output P --ffmpeg F [--fps 2] [--synthetic]
    python ios_helper.py devmode     --udid U

Contrato con Tatana (IosHelper.cs / DvtRecorder.cs; lo fijan los tests):
  * Éxito: una línea JSON en stdout (ensure_ascii=True), código de salida 0.
  * Error: el traceback va a stderr y DESPUÉS una línea
        TATANA_ERROR {"code": "<código>", "detail": "<texto, máx. 300>"}
    con código de salida 2.
  * record: READY en stderr cuando el origen está abierto; se detiene con la línea "stop" por
    stdin (o con EOF de stdin); al terminar escribe DONE {"frames": N, "ffmpeg_exit": X} y sale
    0 si N > 0 y X == 0, o 3 si no.
  * Sin manejo de señales ni separadores de PATH: Tatana pasa la ruta absoluta de ffmpeg.
  * Siempre sale con os._exit (no se cuelga esperando hilos de la pila de pymobiledevice3).
"""

import argparse
import asyncio
import contextlib
import io
import json
import os
import subprocess
import sys
import threading
import time
import traceback
import warnings

warnings.filterwarnings("ignore")

EXIT_OK = 0
EXIT_ERROR = 2
EXIT_EMPTY = 3

DETAIL_MAX = 300
SCREENSHOT_TIMEOUT = 10.0
FRAME_TIMEOUT = 5.0
FFMPEG_WAIT = 60

# Códigos de error (mismos strings que Services/Ios/IosErrors.cs).
APPLE_SERVICE_MISSING = "ios_apple_service_missing"
DEVICE_NOT_FOUND = "ios_device_not_found"
NOT_TRUSTED = "ios_not_trusted"
LOCKED = "ios_locked"
DEVELOPER_MODE_DISABLED = "ios_developer_mode_disabled"
DDI_MOUNT_FAILED = "ios_ddi_mount_failed"
TUNNEL_FAILED = "ios_tunnel_failed"
ADMIN_REQUIRED = "ios_admin_required"
TOOLS_MISSING = "ios_tools_missing"
CAPTURE_FAILED = "ios_capture_failed"


class HelperError(Exception):
    """Error ya clasificado por el helper."""

    def __init__(self, code, detail):
        super().__init__(detail)
        self.code = code
        self.detail = detail


# ── Salida ──────────────────────────────────────────────────────────────────────────────


def _stdout_json(obj):
    sys.stdout.write(json.dumps(obj, ensure_ascii=True) + "\n")
    sys.stdout.flush()


def _stderr_line(text):
    sys.stderr.write(text + "\n")
    sys.stderr.flush()


def _detail(exc):
    text = str(exc).strip() or type(exc).__name__
    text = " ".join(text.split())
    return text[:DETAIL_MAX]


def _emit_error(code, detail):
    payload = json.dumps({"code": code, "detail": (detail or "")[:DETAIL_MAX]}, ensure_ascii=True)
    _stderr_line("TATANA_ERROR " + payload)


def _hard_exit(code):
    try:
        sys.stdout.flush()
        sys.stderr.flush()
    except Exception:
        pass
    os._exit(code)


# ── Clasificación de excepciones (tabla de §4.1) ───────────────────────────────────────


def _exception_classes(*names):
    """Clases de pymobiledevice3.exceptions que existan (una que falte no rompe el helper)."""
    try:
        from pymobiledevice3 import exceptions as pmd_exc
    except Exception:
        return ()
    found = []
    for name in names:
        cls = getattr(pmd_exc, name, None)
        if isinstance(cls, type):
            found.append(cls)
    return tuple(found)


def _isa(exc, *names):
    classes = _exception_classes(*names)
    return bool(classes) and isinstance(exc, classes)


def _classify_one(exc):
    """Código de una excepción puntual, o None si no es ninguna de las conocidas."""
    if isinstance(exc, HelperError):
        return exc.code
    if isinstance(exc, ImportError):
        return TOOLS_MISSING
    if _isa(exc, "ConnectionFailedToUsbmuxdError"):
        return APPLE_SERVICE_MISSING
    if isinstance(exc, ConnectionRefusedError) and "27015" in str(exc):
        return APPLE_SERVICE_MISSING
    if _isa(exc, "DeveloperModeIsNotEnabledError"):
        return DEVELOPER_MODE_DISABLED
    if _isa(exc, "InvalidServiceError"):
        text = str(exc)
        if "com.apple.instruments" in text or "dtservicehub" in text:
            return DEVELOPER_MODE_DISABLED
    # PasswordRequiredError hereda de PairingError: va antes que "no confía".
    if _isa(exc, "PasscodeRequiredError", "PasswordRequiredError"):
        return LOCKED
    if _isa(exc, "NotTrustedError", "NotPairedError", "PairingDialogResponsePendingError",
            "UserDeniedPairingError", "InvalidHostIDError", "PairingError"):
        return NOT_TRUSTED
    if _isa(exc, "NoDeviceConnectedError", "DeviceNotFoundError", "NotConnectedError",
            "ConnectionTerminatedError"):
        return DEVICE_NOT_FOUND
    if _isa(exc, "DeveloperDiskImageNotFoundError"):
        return DDI_MOUNT_FAILED
    try:
        import urllib.error
        if isinstance(exc, urllib.error.URLError):
            return DDI_MOUNT_FAILED
    except Exception:
        pass
    if _isa(exc, "UserspaceTunnelUnavailableError", "TunneldConnectionError"):
        return TUNNEL_FAILED
    if _isa(exc, "AccessDeniedError") or isinstance(exc, PermissionError):
        return ADMIN_REQUIRED
    if isinstance(exc, OSError) and getattr(exc, "winerror", None) == 5:
        return ADMIN_REQUIRED
    return None


def classify(exc, fallback=CAPTURE_FAILED):
    """Recorre la cadena de causas (raise … from …) y devuelve el primer código conocido."""
    seen = set()
    current = exc
    depth = 0
    while current is not None and id(current) not in seen and depth < 8:
        seen.add(id(current))
        code = _classify_one(current)
        if code is not None:
            return code
        current = current.__cause__ or current.__context__
        depth += 1
    return fallback


def _reraise_classified(exc, fallback):
    if isinstance(exc, HelperError):
        raise exc
    raise HelperError(classify(exc, fallback), _detail(exc)) from exc


# ── pymobiledevice3 ─────────────────────────────────────────────────────────────────────


def _major(version):
    try:
        return int(str(version).split(".")[0])
    except (ValueError, IndexError):
        return 0


async def _open_lockdown(udid):
    from pymobiledevice3.lockdown import create_using_usbmux
    return await create_using_usbmux(serial=udid)


async def _close_quietly(obj):
    if obj is None:
        return
    try:
        await obj.close()
    except Exception:
        pass


@contextlib.asynccontextmanager
async def _dvt_screenshot_source(udid):
    """Canal DVT de capturas del iPhone: túnel en modo usuario (iOS 17+) o lockdown (< 17)."""
    from pymobiledevice3.services.dvt.instruments.dvt_provider import DvtProvider
    from pymobiledevice3.services.dvt.instruments.screenshot import Screenshot

    lockdown = await _open_lockdown(udid)
    try:
        if _major(lockdown.product_version) >= 17:
            await _close_quietly(lockdown)
            lockdown = None
            # UserspaceRsdTunnel y no establish_userspace_rsd(): esta última registra un atexit
            # con os._exit(0) (por eso el proceso salía con 0 aunque fallara) y no cierra limpio.
            from pymobiledevice3.remote.userspace_tunnel import UserspaceRsdTunnel
            tunnel = UserspaceRsdTunnel(serial=udid)
            try:
                rsd = await tunnel.aopen()
            except Exception as exc:
                await _close_quietly_tunnel(tunnel)
                _reraise_classified(exc, TUNNEL_FAILED)
            try:
                async with DvtProvider(rsd) as dvt:
                    async with Screenshot(dvt) as shots:
                        yield shots
            finally:
                await _close_quietly_tunnel(tunnel)
        else:
            async with DvtProvider(lockdown) as dvt:
                async with Screenshot(dvt) as shots:
                    yield shots
    finally:
        await _close_quietly(lockdown)


async def _close_quietly_tunnel(tunnel):
    try:
        await tunnel.aclose()
    except Exception:
        pass


class _SyntheticSource:
    """Origen de prueba (autoprueba y CI): PNG grises numerados, sin tocar usbmux."""

    def __init__(self):
        from PIL import Image, ImageDraw  # noqa: F401 (falla temprano si no hay Pillow)
        self._n = 0

    async def get_screenshot(self):
        from PIL import Image, ImageDraw
        self._n += 1
        img = Image.new("RGB", (390, 844), (128, 128, 128))
        try:
            draw = ImageDraw.Draw(img)
            draw.text((24, 24), "Tatana autoprueba - cuadro %d" % self._n, fill=(0, 0, 0))
        except Exception:
            pass
        buf = io.BytesIO()
        img.save(buf, "PNG")
        await asyncio.sleep(0)
        return buf.getvalue()


@contextlib.asynccontextmanager
async def _synthetic_source():
    yield _SyntheticSource()


# ── Subcomandos ─────────────────────────────────────────────────────────────────────────


async def cmd_devices(_args):
    from pymobiledevice3 import usbmux
    devices = await usbmux.list_devices()
    result = []
    seen = set()
    for device in devices:
        if not getattr(device, "is_usb", True):
            continue
        udid = device.serial
        if not udid or udid in seen:
            continue
        seen.add(udid)
        item = {"udid": udid, "name": "", "product_type": "", "product_version": "",
                "imei": "", "phone_number": ""}
        lockdown = None
        try:
            lockdown = await _open_lockdown(udid)
            values = lockdown.all_values or {}
            for key, field in (("DeviceName", "name"), ("ProductType", "product_type"),
                               ("ProductVersion", "product_version"),
                               ("InternationalMobileEquipmentIdentity", "imei"),
                               ("PhoneNumber", "phone_number")):
                value = values.get(key)
                if value is not None:
                    item[field] = str(value)
        except Exception as exc:
            # Un iPhone que falla en lockdown (sin "Confiar", bloqueado) sale solo con su UDID.
            _stderr_line("lockdown %s: %s" % (udid, _detail(exc)))
        finally:
            await _close_quietly(lockdown)
        result.append(item)
    _stdout_json(result)
    return EXIT_OK


async def cmd_prepare(args):
    lockdown = await _open_lockdown(args.udid)
    try:
        major = _major(lockdown.product_version)
        # El Modo Desarrollador existe desde iOS 16; antes no hay nada que comprobar.
        if major >= 16 and not await lockdown.get_developer_mode_status():
            raise HelperError(DEVELOPER_MODE_DISABLED, "DeveloperModeStatus = false")
        image_type = "Personalized" if major >= 17 else "Developer"
        try:
            from pymobiledevice3.exceptions import AlreadyMountedError
        except Exception:  # pragma: no cover
            AlreadyMountedError = ()  # noqa: N806
        try:
            from pymobiledevice3.services.mobile_image_mounter import MobileImageMounterService, auto_mount
            async with MobileImageMounterService(lockdown) as mounter:
                mounted = bool(await mounter.is_image_mounted(image_type))
            if mounted:
                ddi = "already_mounted"
            else:
                try:
                    await auto_mount(lockdown)
                    ddi = "mounted"
                except AlreadyMountedError:
                    ddi = "already_mounted"
        except HelperError:
            raise
        except Exception as exc:
            _reraise_classified(exc, DDI_MOUNT_FAILED)
        _stdout_json({"developer_mode": True, "ddi": ddi})
        return EXIT_OK
    finally:
        await _close_quietly(lockdown)


async def cmd_screenshot(args):
    try:
        os.remove(args.output)
    except OSError:
        pass
    async with _dvt_screenshot_source(args.udid) as shots:
        data = await asyncio.wait_for(shots.get_screenshot(), timeout=SCREENSHOT_TIMEOUT)
    if not data:
        raise HelperError(CAPTURE_FAILED, "el iPhone devolvió una captura vacía")
    with open(args.output, "wb") as fh:
        fh.write(data)
    if not os.path.isfile(args.output) or os.path.getsize(args.output) == 0:
        raise HelperError(CAPTURE_FAILED, "no se pudo escribir la captura")
    _stdout_json({"ok": True})
    return EXIT_OK


async def cmd_devmode(args):
    lockdown = await _open_lockdown(args.udid)
    try:
        if _major(lockdown.product_version) < 16 or await lockdown.get_developer_mode_status():
            status = "enabled"
        else:
            from pymobiledevice3.services.amfi import AmfiService
            amfi = AmfiService(lockdown)
            try:
                # Sin esperar el reinicio: el perito toca "Encender" en el iPhone al prender.
                await amfi.enable_developer_mode(enable_post_restart=False)
                status = "restarting"
            except Exception as exc:
                if not _isa(exc, "DeviceHasPasscodeSetError"):
                    raise
                await amfi.reveal_developer_mode_option_in_ui()
                status = "manual_required"
        _stdout_json({"status": status})
        return EXIT_OK
    finally:
        await _close_quietly(lockdown)


def _watch_stdin(loop, stop_event):
    """Hilo daemon: "stop" o EOF por stdin → detener. Sin señales (no existen en Windows)."""
    try:
        for line in sys.stdin:
            if line.strip().lstrip("﻿").lower() == "stop":
                break
    except Exception:
        pass
    try:
        loop.call_soon_threadsafe(stop_event.set)
    except RuntimeError:
        pass  # el loop ya cerró


async def _capture_loop(source, ffmpeg_proc, fps, stop_event):
    """Devuelve (cuadros, última excepción)."""
    frame_interval = 1.0 / fps
    frames = 0
    last_exc = None
    stop_wait = asyncio.ensure_future(stop_event.wait())
    try:
        while not stop_event.is_set():
            t0 = time.monotonic()
            shot = asyncio.ensure_future(source.get_screenshot())
            done, _ = await asyncio.wait({shot, stop_wait}, timeout=FRAME_TIMEOUT,
                                         return_when=asyncio.FIRST_COMPLETED)
            if shot not in done:
                shot.cancel()
                with contextlib.suppress(BaseException):
                    await shot
                continue  # timeout de la captura o pidieron detener
            try:
                data = shot.result()
            except Exception as exc:
                last_exc = exc
                if stop_event.is_set():
                    break
                await asyncio.sleep(0.2)
                continue
            if not data:
                continue
            try:
                ffmpeg_proc.stdin.write(data)
                ffmpeg_proc.stdin.flush()
            except (BrokenPipeError, OSError, ValueError) as exc:
                last_exc = exc
                break
            frames += 1
            remaining = frame_interval - (time.monotonic() - t0)
            if remaining > 0 and not stop_event.is_set():
                with contextlib.suppress(asyncio.TimeoutError):
                    await asyncio.wait_for(asyncio.shield(stop_wait), timeout=remaining)
    finally:
        if not stop_wait.done():
            stop_wait.cancel()
    return frames, last_exc


def _remove_if_empty(path):
    try:
        if os.path.isfile(path) and os.path.getsize(path) == 0:
            os.remove(path)
    except OSError:
        pass


def _kill(proc):
    if proc is None:
        return
    try:
        proc.kill()
    except Exception:
        pass
    try:
        proc.wait(timeout=10)
    except Exception:
        pass


async def cmd_record(args):
    if not args.ffmpeg or not os.path.isfile(args.ffmpeg):
        raise HelperError(TOOLS_MISSING, "no se encontró ffmpeg en %r" % (args.ffmpeg,))
    fps = args.fps if args.fps and args.fps > 0 else 2.0

    creationflags = 0
    if sys.platform == "win32":
        creationflags = getattr(subprocess, "CREATE_NO_WINDOW", 0x08000000)
    ffmpeg_proc = subprocess.Popen(
        [args.ffmpeg, "-y", "-f", "image2pipe", "-vcodec", "png", "-r", str(fps),
         "-i", "pipe:0", "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2",
         "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "23", args.output],
        stdin=subprocess.PIPE, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
        creationflags=creationflags)

    loop = asyncio.get_running_loop()
    stop_event = asyncio.Event()
    opened = False
    frames = 0
    last_exc = None
    source_cm = _synthetic_source() if args.synthetic else _dvt_screenshot_source(args.udid)
    try:
        async with source_cm as source:
            opened = True
            _stderr_line("READY")
            threading.Thread(target=_watch_stdin, args=(loop, stop_event), daemon=True).start()
            frames, last_exc = await _capture_loop(source, ffmpeg_proc, fps, stop_event)
    except Exception as exc:
        if not opened:
            _kill(ffmpeg_proc)
            _remove_if_empty(args.output)
            raise
        # Falló el cierre del origen después de READY: el MP4 se cierra igual.
        traceback.print_exc(file=sys.stderr)

    try:
        ffmpeg_proc.stdin.close()
    except Exception:
        pass
    try:
        ffmpeg_exit = ffmpeg_proc.wait(timeout=FFMPEG_WAIT)
    except subprocess.TimeoutExpired:
        _kill(ffmpeg_proc)
        ffmpeg_exit = -1

    if frames == 0 and last_exc is not None:
        # Sin cuadros: Tatana usa este código como motivo de ios_recording_empty.
        traceback.print_exception(type(last_exc), last_exc, last_exc.__traceback__, file=sys.stderr)
        _emit_error(classify(last_exc), _detail(last_exc))
    _stderr_line("DONE " + json.dumps({"frames": frames, "ffmpeg_exit": ffmpeg_exit}, ensure_ascii=True))
    return EXIT_OK if frames > 0 and ffmpeg_exit == 0 else EXIT_EMPTY


# ── Entrada ─────────────────────────────────────────────────────────────────────────────


def _parser():
    parser = argparse.ArgumentParser(prog="ios_helper.py")
    sub = parser.add_subparsers(dest="command")
    sub.required = True

    sub.add_parser("devices")

    p = sub.add_parser("prepare")
    p.add_argument("--udid", required=True)

    p = sub.add_parser("screenshot")
    p.add_argument("--udid", required=True)
    p.add_argument("--output", required=True)

    p = sub.add_parser("record")
    p.add_argument("--udid", required=True)
    p.add_argument("--output", required=True)
    p.add_argument("--ffmpeg", required=True)
    p.add_argument("--fps", type=float, default=2.0)
    p.add_argument("--synthetic", action="store_true")

    p = sub.add_parser("devmode")
    p.add_argument("--udid", required=True)
    return parser


HANDLERS = {
    "devices": cmd_devices,
    "prepare": cmd_prepare,
    "screenshot": cmd_screenshot,
    "record": cmd_record,
    "devmode": cmd_devmode,
}


def main(argv):
    args = _parser().parse_args(argv)
    try:
        if not args.command == "record" or not args.synthetic:
            import pymobiledevice3  # noqa: F401
    except ImportError as exc:
        traceback.print_exc(file=sys.stderr)
        _emit_error(TOOLS_MISSING, _detail(exc))
        return EXIT_ERROR
    # Un loop propio y sin cerrarlo: asyncio.run() espera a las tareas y al executor de la pila
    # de pymobiledevice3 al salir, y eso se puede colgar. Se sale con os._exit.
    loop = asyncio.new_event_loop()
    asyncio.set_event_loop(loop)
    try:
        return loop.run_until_complete(HANDLERS[args.command](args))
    except BaseException as exc:  # noqa: BLE001 — todo error sale clasificado
        traceback.print_exc(file=sys.stderr)
        code = exc.code if isinstance(exc, HelperError) else classify(exc)
        detail = exc.detail if isinstance(exc, HelperError) else _detail(exc)
        _emit_error(code, detail)
        return EXIT_ERROR


if __name__ == "__main__":
    try:
        exit_code = main(sys.argv[1:])
    except SystemExit as exc:  # argparse
        exit_code = exc.code if isinstance(exc.code, int) else EXIT_ERROR
    _hard_exit(exit_code)
