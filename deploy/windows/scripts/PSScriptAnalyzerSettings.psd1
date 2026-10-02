# Reglas de PSScriptAnalyzer para los scripts de instalación de Factum.
# Uso: Invoke-ScriptAnalyzer -Path scripts -Settings scripts/PSScriptAnalyzerSettings.psd1
@{
    # Reglas por defecto + compatibilidad con Windows PowerShell 5.1 (el que trae Windows 10)
    # + BOM obligatorio en archivos con caracteres no ASCII.
    IncludeDefaultRules = $true

    # Los scripts son de consola interactiva: Write-Host es lo correcto (colores, transcript).
    ExcludeRules        = @('PSAvoidUsingWriteHost')

    Rules               = @{
        # Sintaxis: nada de ??, ternario, &&/||, etc.
        PSUseCompatibleSyntax         = @{
            Enable         = $true
            TargetVersions = @('5.1')
        }
        # Regla clásica pedida por la SDD. Ojo: solo marca cmdlets que existen en el perfil de
        # referencia y no en el destino; por eso se suman las dos reglas de abajo.
        PSUseCompatibleCmdlets        = @{
            Enable        = $true
            Compatibility = @('desktop-5.1.14393.206-windows')
        }
        # Comandos y tipos .NET disponibles en Windows 10 Pro con Windows PowerShell 5.1.
        PSUseCompatibleCommands       = @{
            Enable         = $true
            TargetProfiles = @('win-48_x64_10.0.17763.0_5.1.17763.316_x64_4.0.30319.42000_framework')
        }
        PSUseCompatibleTypes          = @{
            Enable         = $true
            TargetProfiles = @('win-48_x64_10.0.17763.0_5.1.17763.316_x64_4.0.30319.42000_framework')
        }
        PSUseBOMForUnicodeEncodedFile = @{
            Enable = $true
        }
    }
}
