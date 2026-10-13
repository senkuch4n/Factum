# Checklist — E2E en navegador con celular real

Claude in Chrome con pestaña propia (`tabs_create_mcp`), sesión ya iniciada del usuario, celular por USB.
Expedientes con prefijo `QA-ROBUSTEZ-`. Mirar cada captura; revisar consola y requests fallidos.

1. **Inspección completa**: dispositivo → datos de la causa (acordeón, Siguiente/Editar, ficha viva, 3D,
   precarga) → captura (capturas + **grabación de 10+ min con Tatana**: se detiene, se guarda, se
   previsualiza) → informe → generar ZIP cifrado → listo. Medir tiempos.
2. **Robustez de UI**: recargar en cada paso, botón atrás, doble clic en "Crear caso" y "Generar", dos
   pestañas con el mismo caso, textos enormes/emoji/HTML (cómo salen en la ficha y en el informe), fecha
   futura, expediente repetido, anchos 1366x656 / 1024 / 390, tema claro y oscuro.
3. **Errores**: observar los estados de error existentes sin matar servicios compartidos; listar las
   pruebas físicas que tiene que hacer el usuario.
4. **Historial y Métricas**: buscar el caso QA, filtros, vistas lista/tabla/cuadrícula, "Retomar".
5. **Accesibilidad**: teclado en el paso 2, foco visible, árbol de accesibilidad (`read_page`).

Limpieza: borrar solo los casos QA por id si la UI/API lo permite; si no, dejar la lista exacta de ids.
