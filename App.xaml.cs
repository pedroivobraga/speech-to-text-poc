using Microsoft.UI.Xaml;

namespace PocStt;

/// <summary>
/// Ponto de entrada da aplicação. Mantém uma referência forte à janela principal
/// para evitar que ela seja coletada pelo GC enquanto estiver visível.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Cria e ativa a janela principal quando o app é iniciado.
    /// </summary>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
