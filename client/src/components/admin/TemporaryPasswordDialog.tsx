"use client";

import { useId } from "react";
import { Dialog } from "primereact/dialog";
import { Button } from "primereact/button";
import { KeyRound, ShieldAlert } from "lucide-react";
import { CopyButton } from "@/components/feedback/CopyButton";
import { buildClientMessage } from "@/lib/admin-accounts";

export interface TemporaryPasswordReveal {
  name: string;
  dni: string;
  password: string;
}

interface Props {
  /** `null` = cerrado. El padre lo pone en `null` al cerrar: la temporal no queda en ningún otro lado. */
  reveal: TemporaryPasswordReveal | null;
  onClose: () => void;
}

/**
 * Contraseña temporal recién generada (alta o reset, D7). Se ve una sola
 * vez: el diálogo no se cierra con Escape, con click afuera ni con una X;
 * solo con "Listo, ya la copié". Nunca va a storage, a la URL ni a la consola.
 */
export function TemporaryPasswordDialog({ reveal, onClose }: Props) {
  const uid = useId();
  const descId = `${uid}-desc`;
  const copyWrapId = `${uid}-copy`;
  const message = reveal && typeof window !== "undefined"
    ? buildClientMessage(window.location.origin, reveal.dni, reveal.password)
    : "";

  return (
    <Dialog
      visible={reveal !== null}
      onHide={onClose}
      header={
        <span className="flex items-center gap-3">
          <span className="inline-flex h-9 w-9 shrink-0 items-center justify-center rounded-fx-md bg-fx-accent-soft text-fx-accent-text">
            <KeyRound className="h-4 w-4" aria-hidden="true" />
          </span>
          <span className="min-w-0">Contraseña temporal de {reveal?.name}</span>
        </span>
      }
      modal
      closable={false}
      closeOnEscape={false}
      dismissableMask={false}
      draggable={false}
      resizable={false}
      onShow={() => document.querySelector<HTMLButtonElement>(`[id="${copyWrapId}"] button`)?.focus()}
      pt={{
        root: { "aria-describedby": descId, className: "w-[min(30rem,100%)]" } as React.HTMLAttributes<HTMLDivElement>,
      }}
      footer={<Button label="Listo, ya la copié" onClick={onClose} />}
    >
      {reveal && (
        <div className="flex flex-col gap-4">
          <div className="rounded-fx-lg border border-fx-border-strong bg-fx-surface-1 px-4 py-4 text-center">
            <p className="m-0 mb-1 text-fx-label uppercase text-fx-text-3">
              DNI <span translate="no" className="tabular-nums">{reveal.dni}</span>
            </p>
            <p
              translate="no"
              className="m-0 select-all break-all font-mono text-2xl font-semibold tracking-wider text-fx-text sm:text-[1.75rem]"
            >
              {reveal.password}
            </p>
            <div id={copyWrapId} className="mt-3 flex justify-center">
              <CopyButton text={reveal.password} label="Copiar contraseña temporal" />
            </div>
          </div>

          <p id={descId} className="m-0 flex items-start gap-2 rounded-fx-md bg-fx-warning-soft px-3 py-2.5 text-fx-body-sm text-fx-text">
            <ShieldAlert className="mt-0.5 h-4 w-4 shrink-0 text-fx-warning" aria-hidden="true" />
            <span>
              Es la única vez que vas a ver esta contraseña. Entregásela al cliente por un canal seguro. Va a tener que
              cambiarla en su primer ingreso.
            </span>
          </p>

          <div className="flex flex-col gap-2">
            <p className="m-0 text-fx-label uppercase text-fx-text-3">Mensaje para el cliente</p>
            <p className="m-0 whitespace-pre-wrap rounded-fx-md border border-fx-border bg-fx-surface-1 px-3 py-2 text-xs leading-relaxed text-fx-text-2">
              {message}
            </p>
            <CopyButton
              text={message}
              label="Copiar mensaje para el cliente"
              buttonLabel="Copiar mensaje para el cliente"
            />
          </div>
        </div>
      )}
    </Dialog>
  );
}
