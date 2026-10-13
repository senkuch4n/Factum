# Checklist — API del backend (:8080)

JSON en snake_case_lower. Mapear endpoints leyendo `server/src/Factum.Backend/Controllers`.

1. **Auth**: sin token, token inválido/expirado/firmado con otra clave; IDOR entre `qa-robustez-1` y
   `qa-robustez-2` (casos, archivos, informes, catálogos, perfil); endpoints de admin con usuario común;
   fuerza bruta de login (¿rate limit / lockout?).
2. **Validación** (crear/editar caso, perfil): campos faltantes, tipos erróneos, strings de 100k,
   unicode/emoji/RTL, null bytes, HTML/JS (XSS almacenado que llegue al DOCX o a la web), inyección NoSQL
   (`{"$gt":""}`, operadores en claves), fechas inválidas, JSON malformado, Content-Type equivocado,
   body enorme.
3. **Subidas** (`/api/cases/{id}/files`, `generate/finish`): path traversal en el nombre, nombres
   reservados de Windows, tamaño 0, Content-Length mentido, cortar a mitad (no debe quedar basura en
   `Storage:DataDirectory/.upload-tmp`).
4. **Concurrencia**: 20–50 requests simultáneos sobre el mismo caso, doble submit, generar dos veces en
   paralelo → buscar 500s, duplicados o estados inconsistentes.
5. **Errores**: los 4xx/5xx no filtran stack traces, rutas ni secretos; CORS con Origin no permitido;
   headers de seguridad.
6. **Resiliencia**: manejo de Mongo caído leyendo el código (no detener el contenedor compartido).
