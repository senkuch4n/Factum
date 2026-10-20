#!/usr/bin/env python3
"""Verifica que un tar de `docker save` traiga SOLO linux/amd64 y las imágenes esperadas.

Uso: verificar-tar-imagen.py <imagen.tar> <repo:tag> [<repo:tag> ...]

Misma lógica que la verificación inline de deploy/windows/armar-paquete.sh (recorre index.json del tar
OCI hasta cada config; ignora las atestaciones de buildx), en archivo aparte para no duplicarla por imagen.
"""
import json
import sys
import tarfile


def main():
    tar_path, *esperadas = sys.argv[1:]
    tf = tarfile.open(tar_path)

    def blob(digest):
        alg, h = digest.split(":", 1)
        return json.load(tf.extractfile(f"blobs/{alg}/{h}"))

    plataformas, tags, atestaciones = set(), set(), []

    def recorrer(desc):
        mt = desc.get("mediaType", "")
        ann = desc.get("annotations", {})
        ref = ann.get("io.containerd.image.name") or ann.get("org.opencontainers.image.ref.name")
        if ref:
            tags.add(ref)
        if mt.endswith("image.index.v1+json") or mt.endswith("manifest.list.v2+json"):
            for d in blob(desc["digest"]).get("manifests", []):
                if d.get("annotations", {}).get("vnd.docker.reference.type") == "attestation-manifest":
                    continue
                try:
                    recorrer(d)
                except KeyError:
                    pass  # variante de otra plataforma, no exportada
        elif mt.endswith("image.manifest.v1+json") or mt.endswith("manifest.v2+json"):
            man = blob(desc["digest"])
            if "io.containerd.manifest.subject" in ann or any(
                l.get("mediaType") == "application/vnd.in-toto+json" for l in man.get("layers", [])
            ):
                atestaciones.append(desc["digest"])
                return
            cfg = blob(man["config"]["digest"])
            plataformas.add(f'{cfg.get("os")}/{cfg.get("architecture")}')

    for d in json.load(tf.extractfile("index.json"))["manifests"]:
        recorrer(d)
    if plataformas != {"linux/amd64"}:
        sys.exit(f"ERROR: {tar_path} trae {sorted(plataformas)}, no solo linux/amd64.")
    try:  # formato docker clásico (lo usa docker load de los Docker viejos de las distros)
        manifest = json.load(tf.extractfile("manifest.json"))
        for m in manifest:
            tags.update(m.get("RepoTags") or [])
    except KeyError:
        sys.exit(f"ERROR: {tar_path} no tiene manifest.json: un Docker viejo no lo podría cargar.")
    for e in esperadas:
        if not any(t == e or t.endswith("/" + e) for t in tags):
            sys.exit(f"ERROR: {tar_path} no trae {e} (tags: {sorted(tags)}).")
    print(f"  OK  {', '.join(esperadas)}: solo linux/amd64 (+{len(atestaciones)} atestación/es ignoradas)")


if __name__ == "__main__":
    main()
