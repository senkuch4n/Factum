# `caddy-sitios/` — sitios extra de Caddy

El `Caddyfile` importa `/etc/caddy/sitios/*.caddy` (esta carpeta, montada de solo lectura). En el uso normal está
**vacía** (solo este `LEEME.md`): Caddy avisa "No files matching import glob pattern" en el log y arranca igual.

Se usa **solo durante un cambio de dominio** (guía `docs/despliegue-nube.md`, sección 15, fase B), para que el dominio
anterior redirija al nuevo. En el VPS se crea `anterior.caddy` con este bloque, cambiando `<DOMINIO-ANTERIOR>`:

```caddyfile
<DOMINIO-ANTERIOR> {
	redir https://{$FACTUM_DOMINIO}{uri} permanent
}
```

y se aplica con `deploy/cloud/scripts/compose.sh up -d --force-recreate caddy`. Caddy sigue renovando el certificado
del dominio anterior mientras el bloque exista (el DNS del anterior tiene que seguir apuntando al VPS).

En la fase C (semanas después) se borra el archivo y se repite el `up -d --force-recreate caddy`.

Los `*.caddy` reales **no se versionan** (`.gitignore`): son configuración de un servidor concreto.
