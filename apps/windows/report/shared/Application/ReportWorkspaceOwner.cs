using Fowan.Todo.Shared.Models;

namespace Fowan.Report.Shared.Application;

/// <summary>Application-layer owner that creates and exposes the report's sole mutable workspace.</summary>
public sealed class ReportWorkspaceOwner
{
    public ReportWorkspaceOwner(IReportTodoReader todoReader, IReportGenerationRecordStore? recordStore = null)
    {
        Workspace = new ReportWorkspace(todoReader, recordStore);
    }

    public ReportWorkspace Workspace { get; }
}
