"use client";

import { useCallback, useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { ExpertProfile, ExpertProfileRequest, ProfileFormData } from "@/types";

/** Perfil del perito (`GET`/`PUT /api/profile`). Lo usan la tarjeta del paso 2 y el diálogo del menú. */
export function useExpertProfile({ autoLoad = true }: { autoLoad?: boolean } = {}) {
  const [profile, setProfile] = useState<ExpertProfile | null>(null);
  const [loading, setLoading] = useState(autoLoad);
  const [error, setError] = useState("");

  const reload = useCallback(async () => {
    setLoading(true);
    setError("");
    try {
      const p = await api.getProfile();
      setProfile(p);
      return p;
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo cargar tu perfil de perito");
      return null;
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { if (autoLoad) void reload(); }, [autoLoad, reload]);

  /** Guarda (trim incluido) y devuelve el perfil actualizado; tira `ApiError` si falla. */
  const save = useCallback(async (req: ExpertProfileRequest) => {
    const saved = await api.saveProfile({
      nombre: req.nombre.trim(),
      matricula: req.matricula.trim(),
      profesion: req.profesion.trim(),
      caracter: req.caracter.trim(),
      tratamiento: req.tratamiento,
    });
    setProfile(saved);
    return saved;
  }, []);

  return { profile, loading, error, save, reload };
}

/** Formulario editable a partir del perfil del servidor. */
export function profileToForm(p: ExpertProfile | null): ProfileFormData {
  return {
    nombre: p?.nombre ?? "",
    matricula: p?.matricula ?? "",
    profesion: p?.profesion ?? "",
    caracter: p?.caracter ?? "",
    tratamiento: p?.tratamiento === "suscripta" ? "suscripta" : "suscripto",
  };
}

export function sameProfile(a: ProfileFormData, b: ProfileFormData): boolean {
  return a.nombre.trim() === b.nombre.trim()
    && a.matricula.trim() === b.matricula.trim()
    && a.profesion.trim() === b.profesion.trim()
    && a.caracter.trim() === b.caracter.trim()
    && a.tratamiento === b.tratamiento;
}
