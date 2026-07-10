namespace ConnectOnion.WinUIClient.Views;

public interface IFindHost
{
    event System.EventHandler? FindStateChanged;

    string FindStatusText { get; }

    bool HasFindMatches { get; }

    void OpenFind();

    void CloseFind();

    void SetFindQuery(string query);

    void SelectNextFindMatch();

    void SelectPreviousFindMatch();
}
