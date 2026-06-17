using System;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;

namespace PocStt.Speech;

/// <summary>Utilitários de erro e ambiente compartilhados entre os motores.</summary>
public static class SttErrors
{
    // HRESULTs conhecidos da pilha de fala do Windows.
    private const int HResultPrivacyStatementDeclined = unchecked((int)0x80045509);
    private const int HResultAccessDenied = unchecked((int)0x80070005);

    /// <summary>Converte uma exceção em uma mensagem amigável e acionável.</summary>
    public static string Describe(Exception ex) => ex.HResult switch
    {
        HResultPrivacyStatementDeclined =>
            "\"Online speech recognition\" is turned off. Turn it on in " +
            "Settings > Privacy & security > Speech — or use an offline engine.",

        HResultAccessDenied =>
            "Microphone access denied. Allow the microphone for desktop apps in " +
            "Settings > Privacy & security > Microphone.",

        _ => $"Speech recognition failed: {ex.Message}"
    };

    /// <summary>Verifica se há ao menos um dispositivo de captura de áudio.</summary>
    public static async Task<bool> IsMicrophonePresentAsync()
    {
        try
        {
            DeviceInformationCollection devices =
                await DeviceInformation.FindAllAsync(DeviceClass.AudioCapture);
            return devices.Count > 0;
        }
        catch
        {
            // Se a enumeração falhar, deixe o motor tentar e reporte erros reais.
            return true;
        }
    }
}
