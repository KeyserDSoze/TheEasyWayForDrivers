using System.IO;
using System.Drawing;
using System.Windows.Forms;

namespace TheEasyWayForDrivers.Setup;

public sealed class SetupForm : Form
{
    private readonly SetupMode _mode;
    private readonly ProgressBar _progressBar;
    private readonly Label _statusLabel;
    private readonly Button _primaryButton;
    private readonly Button _cancelButton;
    private readonly Label _titleLabel;
    private bool _completed;

    public SetupForm(
        SetupMode mode)
    {
        _mode = mode;

        Text = ModeTitle(mode);
        Width = 720;
        Height = 470;
        MinimumSize = new Size(720, 470);
        MaximumSize = new Size(720, 470);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(244, 246, 248);
        Font = new Font("Segoe UI", 9F);
        MaximizeBox = false;

        var executablePath =
            Environment.ProcessPath;

        if (!string.IsNullOrWhiteSpace(executablePath) &&
            File.Exists(executablePath))
        {
            var extractedIcon =
                Icon.ExtractAssociatedIcon(
                    executablePath);

            if (extractedIcon is not null)
            {
                Icon = extractedIcon;
            }
        }

        var header =
            new Panel
            {
                Dock = DockStyle.Top,
                Height = 132,
                BackColor = Color.FromArgb(17, 24, 39)
            };

        var iconBox =
            new PictureBox
            {
                Left = 28,
                Top = 26,
                Width = 76,
                Height = 76,
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = Icon?.ToBitmap()
            };

        var brand =
            new Label
            {
                AutoSize = true,
                Left = 126,
                Top = 27,
                Text = "OmegaDrive",
                ForeColor = Color.White,
                Font = new Font(
                    "Segoe UI",
                    24F,
                    FontStyle.Bold)
            };

        var subtitle =
            new Label
            {
                AutoSize = true,
                Left = 129,
                Top = 72,
                Text = "DRIVER MANAGER",
                ForeColor = Color.FromArgb(96, 165, 250),
                Font = new Font(
                    "Segoe UI",
                    9F,
                    FontStyle.Bold)
            };

        var tagline =
            new Label
            {
                AutoSize = true,
                Left = 129,
                Top = 94,
                Text = "Drivers. Done right.",
                ForeColor = Color.FromArgb(209, 213, 219)
            };

        header.Controls.Add(iconBox);
        header.Controls.Add(brand);
        header.Controls.Add(subtitle);
        header.Controls.Add(tagline);

        _titleLabel =
            new Label
            {
                AutoSize = true,
                Left = 34,
                Top = 166,
                Text = ModeHeading(mode),
                ForeColor = Color.FromArgb(15, 23, 42),
                Font = new Font(
                    "Segoe UI",
                    18F,
                    FontStyle.Bold)
            };

        var description =
            new Label
            {
                Left = 36,
                Top = 212,
                Width = 640,
                Height = 50,
                Text = ModeDescription(mode),
                ForeColor = Color.FromArgb(71, 85, 105)
            };

        _statusLabel =
            new Label
            {
                Left = 36,
                Top = 285,
                Width = 640,
                Height = 42,
                Text = "Pronto.",
                ForeColor = Color.FromArgb(51, 65, 85)
            };

        _progressBar =
            new ProgressBar
            {
                Left = 36,
                Top = 337,
                Width = 640,
                Height = 12,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Style = ProgressBarStyle.Continuous
            };

        _primaryButton =
            new Button
            {
                Left = 482,
                Top = 375,
                Width = 194,
                Height = 38,
                Text = ModeAction(mode),
                BackColor = Color.FromArgb(15, 108, 189),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font(
                    "Segoe UI",
                    9F,
                    FontStyle.Bold)
            };

        _primaryButton.FlatAppearance.BorderColor =
            Color.FromArgb(15, 108, 189);

        _primaryButton.Click +=
            PrimaryButton_Click;

        _cancelButton =
            new Button
            {
                Left = 366,
                Top = 375,
                Width = 104,
                Height = 38,
                Text = "Annulla"
            };

        _cancelButton.Click +=
            (_, _) => Close();

        Controls.Add(header);
        Controls.Add(_titleLabel);
        Controls.Add(description);
        Controls.Add(_statusLabel);
        Controls.Add(_progressBar);
        Controls.Add(_cancelButton);
        Controls.Add(_primaryButton);

        FormClosing += SetupForm_FormClosing;
    }

    public int ExitCode { get; private set; }

    private async void PrimaryButton_Click(
        object? sender,
        EventArgs e)
    {
        if (_completed)
        {
            Close();
            return;
        }

        _primaryButton.Enabled = false;
        _cancelButton.Enabled = false;
        ControlBox = false;

        var progress =
            new Progress<SetupProgress>(
                value =>
                {
                    _progressBar.Value =
                        Math.Clamp(
                            value.Percent,
                            0,
                            100);

                    _statusLabel.Text =
                        value.Message;
                });

        try
        {
            var engine =
                new SetupEngine(progress);

            await engine.ExecuteAsync(
                _mode,
                CancellationToken.None);

            ExitCode = 0;
            _completed = true;
            _progressBar.Value = 100;
            _titleLabel.Text = SuccessHeading(_mode);
            _statusLabel.Text = SuccessMessage(_mode);
            _primaryButton.Text = "Chiudi";
            _primaryButton.Enabled = true;
            ControlBox = true;
        }
        catch (Exception exception)
        {
            ExitCode = 1;
            _completed = true;
            _titleLabel.Text = "Operazione non completata";
            _statusLabel.Text = exception.Message;
            _primaryButton.Text = "Chiudi";
            _primaryButton.Enabled = true;
            ControlBox = true;

            MessageBox.Show(
                this,
                exception.Message,
                "OmegaDrive",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void SetupForm_FormClosing(
        object? sender,
        FormClosingEventArgs e)
    {
        if (!_primaryButton.Enabled &&
            !_completed)
        {
            e.Cancel = true;
        }
    }

    private static string ModeTitle(
        SetupMode mode) =>
        mode switch
        {
            SetupMode.Update =>
                "Aggiorna OmegaDrive",
            SetupMode.Uninstall =>
                "Disinstalla OmegaDrive",
            _ =>
                "Installa OmegaDrive"
        };

    private static string ModeHeading(
        SetupMode mode) =>
        mode switch
        {
            SetupMode.Update =>
                "Aggiornamento OmegaDrive",
            SetupMode.Uninstall =>
                "Disinstallazione OmegaDrive",
            _ =>
                "Installazione OmegaDrive"
        };

    private static string ModeAction(
        SetupMode mode) =>
        mode switch
        {
            SetupMode.Update => "Aggiorna",
            SetupMode.Uninstall => "Disinstalla",
            _ => "Installa"
        };

    private static string ModeDescription(
        SetupMode mode) =>
        mode switch
        {
            SetupMode.Update =>
                "Aggiorna applicazione e servizio preservando le preferenze personali e creando un rollback dell'ultima versione funzionante.",
            SetupMode.Uninstall =>
                "Rimuove OmegaDrive, il servizio Windows e le registrazioni di avvio dal computer.",
            _ =>
                "Installa OmegaDrive Driver Manager, il servizio privilegiato e l'avvio opzionale nell'area di notifica."
        };

    private static string SuccessHeading(
        SetupMode mode) =>
        mode switch
        {
            SetupMode.Update => "OmegaDrive aggiornato",
            SetupMode.Uninstall => "OmegaDrive rimosso",
            _ => "OmegaDrive installato"
        };

    private static string SuccessMessage(
        SetupMode mode) =>
        mode switch
        {
            SetupMode.Update =>
                "Aggiornamento completato. OmegaDrive è stato riavviato.",
            SetupMode.Uninstall =>
                "Disinstallazione completata.",
            _ =>
                "Installazione completata. OmegaDrive è stato avviato."
        };
}
