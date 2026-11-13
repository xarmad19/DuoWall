using System;
using System.Media;
using System.Windows;
using System.Windows.Threading;
using DuoWall.Core.Widgets;

namespace DuoWall.App.Windows;

public partial class ReminderAlertWindow : Window
{
    private readonly CalendarAgendaEntry _entry;
    private readonly Action<CalendarAgendaEntry, bool> _onAction;
    private readonly DispatcherTimer? _alarmTimer;

    public ReminderAlertWindow(CalendarAgendaEntry entry, Action<CalendarAgendaEntry, bool> onAction)
    {
        InitializeComponent();
        _entry = entry;
        _onAction = onAction;

        KindBadgeText.Text = entry.Kind.ToString().ToUpperInvariant();
        ModeBadgeText.Text = entry.PlayAlarmSound ? "Playing Alert Sound" : "Silent Reminder";
        TitleText.Text = entry.Title;
        MetaText.Text = $"{entry.DateIso} at {entry.StartTimeText} ({entry.DurationMinutes} min)" +
                        (string.IsNullOrWhiteSpace(entry.Notes) ? string.Empty : $"\n{entry.Notes}");

        var wa = SystemParameters.WorkArea;
        Left = wa.Right - Width - 22;
        Top = wa.Bottom - 180;

        if (entry.PlayAlarmSound)
        {
            PlayChime();
            _alarmTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
            _alarmTimer.Tick += (_, _) => PlayChime();
            _alarmTimer.Start();
        }

        Closed += (_, _) => _alarmTimer?.Stop();
    }

    private static void PlayChime()
    {
        try { SystemSounds.Exclamation.Play(); } catch { }
    }

    private void Snooze_Click(object sender, RoutedEventArgs e)
    {
        _alarmTimer?.Stop();
        _onAction(_entry, true);
        Close();
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        _alarmTimer?.Stop();
        _onAction(_entry, false);
        Close();
    }
}

