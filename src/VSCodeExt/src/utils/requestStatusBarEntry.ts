import { StatusBarAlignment, StatusBarItem, window } from 'vscode';

export class RequestStatusEntry {
    private readonly currentStatusEntry: StatusBarItem;
    private readonly currentProgressEntry: StatusBarItem;

    public constructor() {
        this.currentStatusEntry = window.createStatusBarItem('status', StatusBarAlignment.Left);
        this.currentStatusEntry.name = 'Current State';
        this.currentProgressEntry = window.createStatusBarItem('progress', StatusBarAlignment.Left);
        this.currentProgressEntry.name = 'Progress';
    }

    public dispose() {
        this.currentStatusEntry.dispose();
    }

    public updateStatus(status: string, command?: string) {
        if (status == null || status == "")
            this.currentStatusEntry.hide();
            if (status == "Completed" || status == "Error")
            this.currentProgressEntry.hide();
        this.showStatusEntry(this.currentStatusEntry, status, command);
    }

    public updateProgress(progress: string) {
        if (progress == null || progress == "")
            this.currentProgressEntry.hide();
        this.showStatusEntry(this.currentProgressEntry, progress);
    }

    private showStatusEntry(statusBar: StatusBarItem, text: string, command?: string) {
        statusBar.text = text;
        statusBar.tooltip = command;
        statusBar.command = command;
        statusBar.show();
    }
}