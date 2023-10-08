import { StatusBarAlignment, StatusBarItem, window } from 'vscode';

export class RequestStatusEntry {
    private readonly currentStatusEntry: StatusBarItem;

    public constructor() {
        this.currentStatusEntry = window.createStatusBarItem('status', StatusBarAlignment.Left);
        this.currentStatusEntry.name = 'Current State';
    }

    public dispose() {
        this.currentStatusEntry.dispose();
    }

    public update(status: string, command?: string) {
        if (status == null || status == "")
            this.currentStatusEntry.hide();
        this.showStatusEntry(status, command);
    }

    private showStatusEntry(text: string, tooltip?: string, command?: string) {
        this.currentStatusEntry.text = text;
        this.currentStatusEntry.tooltip = tooltip;
        this.currentStatusEntry.command = command;
        this.currentStatusEntry.show();
    }
}