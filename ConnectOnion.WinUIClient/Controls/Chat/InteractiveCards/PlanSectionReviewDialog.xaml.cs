using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ConnectOnion.WinUIClient.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ConnectOnion.WinUIClient.Controls;

public sealed partial class PlanSectionReviewDialog : ContentDialog
{
    public PlanSectionReviewDialog(string markdown)
    {
        Sections = new ObservableCollection<PlanReviewSectionItem>(
            PlanReviewSections.Parse(markdown)
                .Select(section => new PlanReviewSectionItem(section.Heading, section.Markdown)));
        InitializeComponent();
    }

    public ObservableCollection<PlanReviewSectionItem> Sections { get; }

    public string BuildFeedback()
    {
        var entries = Sections
            .Where(section => section.NeedsChanges || !string.IsNullOrWhiteSpace(section.Feedback))
            .Select(section =>
            {
                var feedback = section.Feedback.Trim();
                return feedback.Length == 0
                    ? $"[{section.Heading}] Needs changes."
                    : $"[{section.Heading}] {feedback}";
            });
        return string.Join(Environment.NewLine, entries);
    }

    private void ApproveSection_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PlanReviewSectionItem section)
            section.SetDecision(approved: true);
    }

    private void RequestSectionChanges_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PlanReviewSectionItem section)
            section.SetDecision(approved: false);
    }
}

public sealed partial class PlanReviewSectionItem : ObservableObject
{
    public PlanReviewSectionItem(string heading, string markdown)
    {
        Heading = heading;
        Markdown = markdown;
        Feedback = "";
    }

    public string Heading { get; set; }
    public string Markdown { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApproveAccessibilityName))]
    [NotifyPropertyChangedFor(nameof(ChangesAccessibilityName))]
    public partial bool IsApproved { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApproveAccessibilityName))]
    [NotifyPropertyChangedFor(nameof(ChangesAccessibilityName))]
    public partial bool NeedsChanges { get; private set; }

    [ObservableProperty]
    public partial string Feedback { get; set; }

    public string ApproveAccessibilityName
        => $"{Heading}: looks good, {(IsApproved ? "selected" : "not selected")}";
    public string ChangesAccessibilityName
        => $"{Heading}: needs changes, {(NeedsChanges ? "selected" : "not selected")}";
    public string FeedbackAccessibilityName => $"Feedback for {Heading}";

    public void SetDecision(bool approved)
    {
        IsApproved = approved;
        NeedsChanges = !approved;
    }
}
