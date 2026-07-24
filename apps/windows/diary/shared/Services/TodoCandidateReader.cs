using Fowan.Diary.Shared.Models;
using Fowan.Todo.Shared.Models;
using System.Text.Json;

namespace Fowan.Diary.Shared.Services;

public static class TodoCandidateReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<TodoCandidate> LoadOpenCandidates(int maxCount)
    {
        try
        {
            var dataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Fowan",
                "Todo",
                "todo-data.json");

            if (!File.Exists(dataPath))
            {
                return [];
            }

            var data = JsonSerializer.Deserialize<TodoData>(File.ReadAllText(dataPath), JsonOptions);
            if (data?.Tasks is null)
            {
                return [];
            }

            var listNames = data.Lists?
                .Where(list => !string.IsNullOrWhiteSpace(list.Id))
                .GroupBy(list => list.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.Ordinal)
                ?? [];

            return data.Tasks
                .Where(task => !task.IsCompleted && !string.IsNullOrWhiteSpace(task.Title))
                .OrderBy(task => task.StartDate == default ? DateTime.MaxValue : task.StartDate.Date)
                .ThenByDescending(task => task.UpdatedAt)
                .Take(Math.Max(0, maxCount))
                .Select(task => new TodoCandidate
                {
                    Id = task.Id,
                    Title = task.Title.Trim(),
                    ListName = listNames.TryGetValue(task.ListId, out var listName) && !string.IsNullOrWhiteSpace(listName)
                        ? listName
                        : "待办",
                    StartDate = task.StartDate == default ? DateTime.Today : task.StartDate.Date
                })
                .ToList();
        }
        catch
        {
            return [];
        }
    }
}
