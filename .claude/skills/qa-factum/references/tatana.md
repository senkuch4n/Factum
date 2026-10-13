# Checklist — agente Tatana (instancia mock propia)

Levantar: `dotnet run --project server/src/Factum.Agent -- --mock --port 8766 --data <scratchpad>/tatana-qa-data`
(verificar flags en `Program.cs`). Se puede matar y reiniciar; al final, matarla y borrar su `--data`.

1. **Seguridad local**: Origin no permitido (CORS / Private Network Access), path traversal en
   `files/{filename}`, `evidence/import`, `evidence/upload?filename=`, `DELETE files` (`../`, absolutos,
   `%2e%2e`, reservados de Windows, unicode), caseId no-Guid, nombres larguísimos.
2. **Subidas**: tamaño 0, Content-Length mentido, corte a mitad (sin parciales), mismo nombre en
   paralelo, `Agent:MaxUploadBytes` bajo, poco espacio libre, 20 subidas concurrentes.
3. **Ciclo de vida**: matar el proceso a mitad de subida / ZIP / post-proceso de video y verificar la
   limpieza al rearrancar (`.postprocess`, `.upload-tmp`); `/agent/state` (`busy`,
   `background_operations`) coherente.
4. **ZIP de evidencia**: caso vacío, nombres raros, archivo borrado entre hash y zip, contraseña
   vacía/rara; verificar con `7zz t` y `python3 zipfile`.
5. **WebSocket**: muchos clientes, cliente que no lee, mensajes malformados, reconexión.
6. **Post-proceso de video** (mock iOS): cancelar al borrar, timeout corto, `GET /video-postprocess/status`.
