"use client";

import { useState } from "react";
import { Button } from "primereact/button";
import { SelectButton } from "primereact/selectbutton";
import { ChevronRight } from "lucide-react";
import { cn } from "@/lib/utils";
import { IOSGuide } from "./usb-guide/IOSGuide";
import { AndroidGuide } from "./usb-guide/AndroidGuide";

interface Props { onDone: () => void; }

type Platform = "android" | "ios";

const PLATFORMS: { value: Platform; label: string; img: string }[] = [
  { value: "android", label: "Android", img: "/android.svg" },
  { value: "ios", label: "iPhone", img: "/apple.svg" },
];

export function USBGuide({ onDone }: Props) {
  const [platform, setPlatform] = useState<Platform>("android");

  return (
    <div className="space-y-5">
      <div>
        <h2 className="text-fx-h3 text-fx-text">Preparar el dispositivo</h2>
        <p className="mt-1.5 text-fx-body-sm text-fx-text-2">
          Guía para habilitar la conexión USB del celular a inspeccionar.
        </p>
      </div>

      <SelectButton
        value={platform}
        onChange={(e) => e.value && setPlatform(e.value as Platform)}
        allowEmpty={false}
        options={PLATFORMS}
        optionLabel="label"
        optionValue="value"
        itemTemplate={(o: (typeof PLATFORMS)[number]) => (
          <>
            {/* contenido de imagen: SVG monocromo negro. Sigue el color del texto de la
                opción: sobre el acento (seleccionada) va claro en modo claro y oscuro en
                modo oscuro; sin seleccionar, al revés. */}
            <img
              src={o.img}
              alt=""
              className={cn(
                "h-4 w-4 object-contain",
                o.value === platform ? "invert dark:invert-0" : "opacity-80 dark:invert",
              )}
            />
            <span>{o.label}</span>
          </>
        )}
        pt={{
          root: { className: "w-full", "aria-label": "Tipo de dispositivo" },
          button: { className: "flex-1 min-h-10" },
        }}
      />

      {platform === "android" ? <AndroidGuide onDone={onDone} /> : <IOSGuide onDone={onDone} />}

      <Button
        text
        severity="secondary"
        label="El dispositivo ya está listo, continuar"
        icon={<ChevronRight className="h-4 w-4" aria-hidden="true" />}
        iconPos="right"
        onClick={onDone}
        className="w-full"
      />
    </div>
  );
}
