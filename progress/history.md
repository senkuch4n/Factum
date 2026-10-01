# Historial de HU cerradas

Log append-only. Al cerrar cada HU (aprobada o bloqueada), el orquestador
agrega una sección `## <id> — APROBADA|BLOQUEADA (fecha)` con qué se hizo,
commits, modelos usados, intentos de revisión y lo que quedó pendiente.
