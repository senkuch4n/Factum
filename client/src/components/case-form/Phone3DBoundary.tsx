"use client";

import { Component, type ReactNode } from "react";

interface Props {
  onError: (reason: string) => void;
  children: ReactNode;
}

let warned = false;

/**
 * Atrapa cualquier error del teléfono 3D (chunk que no carga, `WebGLRenderer`
 * que no se puede crear, error de render de la escena) y deja de renderizar:
 * `DeviceStage` vuelve al marco 2D (§6.8 D8). El aviso a consola no lleva
 * datos de la causa.
 */
export class Phone3DBoundary extends Component<Props, { failed: boolean }> {
  state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  componentDidCatch(err: unknown) {
    if (!warned) {
      warned = true;
      console.warn("[factum] Vista 3D del equipo desactivada:", err instanceof Error ? err.message : String(err));
    }
    this.props.onError(String(err));
  }

  render() {
    return this.state.failed ? null : this.props.children;
  }
}
