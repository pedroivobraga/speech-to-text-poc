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
            "O \"Reconhecimento de fala on-line\" está desativado. Ative em " +
            "Configurações > Privacidade e segurança > Fala — ou use um motor offline.",

        HResultAccessDenied =>
            "Acesso ao microfone negado. Permita o microfone para aplicativos da área " +
            "de trabalho em Configurações > Privacidade e segurança > Microfone.",

        _ => $"Falha no reconhecimento de fala: {ex.Message}"
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
