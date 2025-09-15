# Reglas Personalizadas de Ejecución

## Regla de Debug Automático
Siempre que el usuario reporte un error o problema:

1. **PRIMERO**: Revisar automáticamente los logs de la aplicación
   - Buscar archivos de log recientes en el directorio del proyecto
   - Examinar archivos `sample_pdf_text_*.txt` para datos de debug
   - Verificar output de consola y logs de error
   - Analizar patrones de fallo antes de pedir información adicional al usuario

2. **LUEGO**: Investigar el código relevante basándose en lo que muestren los logs

3. **FINALMENTE**: Proporcionar una solución basada en evidencia de los logs

## Reglas de Testing
- Siempre ejecutar tests después de cambios importantes
- Validar con archivos ZIP reales del directorio `Resources/`
- Verificar que los logs muestren información útil durante las pruebas

## Reglas de Logging
- Todos los errores deben ser loggeados con contexto suficiente
- Usar niveles apropiados: Trace para debug detallado, Debug para desarrollo, Info para operaciones normales
- Incluir nombres de archivo y números de línea cuando sea relevante para debugging

## Regla de Proactividad en Debugging
NO esperar que el usuario proporcione logs o información de debug. Ir proactivamente a buscar y analizar esta información cuando se reporte cualquier problema.