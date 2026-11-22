using System.Windows;
using MftParser.Ui;

namespace MftParser;

public partial class App : Application {
    protected override void OnStartup(StartupEventArgs e) {
        Theme.Apply(false);
        base.OnStartup(e);
    }
}
