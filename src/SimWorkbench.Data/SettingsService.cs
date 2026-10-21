using System.Text.Json;
using SimWorkbench.Core.Logic;
using SimWorkbench.Core.Models;
using SimWorkbench.Core.Services;

namespace SimWorkbench.Data;

/// <summary>设置与模块管理：所有界面配置持久化，模块启停只影响可用性，不删除数据。</summary>
public sealed class SettingsService : ISettingsService, IModuleService
{
    public const string SettingsKey = "app.settings";

    private readonly WorkbenchDatabase _database;
    private readonly object _lock = new();
    private AppSettings? _cached;

    public SettingsService(WorkbenchDatabase database) => _database = database;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    public AppSettings Load()
    {
        lock (_lock)
        {
            if (_cached is not null)
            {
                return _cached.Clone();
            }

            AppSettings? settings = null;
            using (var connection = _database.Open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT value FROM settings WHERE key = $key LIMIT 1;";
                command.Parameters.AddWithValue("$key", SettingsKey);
                var json = command.ExecuteScalar() as string;
                if (!string.IsNullOrWhiteSpace(json))
                {
                    try
                    {
                        settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                    }
                    catch (JsonException ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Settings] 设置解析失败，使用默认设置：{ex.Message}");
                    }
                }
            }

            settings ??= Defaults.CreateDefaultSettings();
            Reconcile(settings);
            _cached = settings;
            return settings.Clone();
        }
    }

    /// <summary>与当前的模块/卡片/导航注册表对齐，兼容升级后新增的条目。</summary>
    private static void Reconcile(AppSettings settings)
    {
        var cardDefs = Defaults.CreateHomeCardDefinitions();
        var knownCardIds = cardDefs.Select(d => d.Id).ToHashSet();
        var cards = settings.HomeCards
            .Where(c => knownCardIds.Contains(c.CardId))
            .GroupBy(c => c.CardId)
            .Select(g => g.First())
            .OrderBy(c => c.Order)
            .ToList();
        foreach (var def in cardDefs)
        {
            if (cards.All(c => c.CardId != def.Id))
            {
                cards.Add(new HomeCardSetting { CardId = def.Id, IsVisible = true });
            }
        }

        for (var i = 0; i < cards.Count; i++)
        {
            cards[i].Order = i;
        }

        settings.HomeCards = cards;

        var navOrder = Defaults.DefaultNavOrder;
        var knownNav = settings.NavItems.ToDictionary(n => n.ModuleId);
        var navItems = new List<NavItemSetting>();
        foreach (var key in navOrder)
        {
            if (knownNav.TryGetValue(key, out var existing))
            {
                navItems.Add(existing);
            }
            else
            {
                navItems.Add(new NavItemSetting { ModuleId = key, IsVisible = true });
            }
        }

        // 保留用户自定义的排序
        var ordered = settings.NavItems
            .Where(n => navItems.Any(x => x.ModuleId == n.ModuleId))
            .OrderBy(n => n.Order)
            .ToList();
        var missing = navItems.Where(n => ordered.All(o => o.ModuleId != n.ModuleId)).ToList();
        ordered.AddRange(missing);
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Order = i;
        }

        settings.NavItems = ordered;

        var definitions = Defaults.CreateModuleDefinitions();
        var knownModules = settings.Modules.ToDictionary(m => m.ModuleId);
        var modules = new List<ModuleSetting>();
        foreach (var def in definitions)
        {
            if (knownModules.TryGetValue(def.Id, out var existing))
            {
                modules.Add(existing);
            }
            else
            {
                modules.Add(new ModuleSetting { ModuleId = def.Id, IsEnabled = def.IsImplemented });
            }
        }

        settings.Modules = modules;
        if (!navOrder.Contains(settings.StartPage))
        {
            settings.StartPage = PageKeys.Workbench;
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_lock)
        {
            Reconcile(settings);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            using var connection = _database.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO settings (key, value) VALUES ($key, $value) ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
            command.Parameters.AddWithValue("$key", SettingsKey);
            command.Parameters.AddWithValue("$value", json);
            command.ExecuteNonQuery();
            _cached = settings.Clone();
        }
    }

    public void ResetLayout()
    {
        var current = Load();
        var defaults = Defaults.CreateDefaultSettings();
        current.HomeCards = defaults.HomeCards;
        current.NavItems = defaults.NavItems;
        current.Modules = defaults.Modules;
        current.StartPage = PageKeys.Workbench;
        current.LastLayoutResetAt = DateTime.Now;
        Save(current);
    }

    public string GetStorageDescription() => $"数据库：{AppPaths.DatabasePath}";

    // ---------------- IModuleService ----------------

    public IReadOnlyList<ModuleRuntimeState> GetAll()
    {
        var settings = Load();
        return Defaults.CreateModuleDefinitions().Select(def => BuildState(def, settings)).ToList();
    }

    public ModuleRuntimeState Get(string moduleId) =>
        GetAll().FirstOrDefault(m => m.Definition.Id == moduleId)
        ?? throw new InvalidOperationException($"未知模块：{moduleId}");

    private static ModuleRuntimeState BuildState(ModuleDefinition def, AppSettings settings)
    {
        var moduleSetting = settings.Modules.FirstOrDefault(m => m.ModuleId == def.Id);
        var navSetting = settings.NavItems.FirstOrDefault(n => n.ModuleId == def.Id);
        var enabled = def.IsCore || (def.IsImplemented && (moduleSetting?.IsEnabled ?? true));
        var navVisible = def.IsImplemented
                         && enabled
                         && (moduleSetting?.IsEnabled ?? true || def.IsCore)
                         && (navSetting?.IsVisible ?? true);

        var status = !def.IsImplemented
            ? ModuleStatus.Unavailable
            : enabled ? ModuleStatus.Enabled : ModuleStatus.Disabled;
        var statusText = status switch
        {
            ModuleStatus.Enabled => "已启用",
            ModuleStatus.Disabled => "已停用",
            ModuleStatus.NotConfigured => "未配置",
            _ => "不可用（未实现）",
        };

        return new ModuleRuntimeState
        {
            Definition = def,
            IsEnabled = enabled,
            ShowInNav = navVisible,
            Status = status,
            StatusText = statusText,
        };
    }

    public bool IsNavVisible(string moduleId) => Get(moduleId).ShowInNav;

    public bool IsEnabled(string moduleId) => Get(moduleId).IsEnabled;

    public void SetEnabled(string moduleId, bool enabled)
    {
        var def = Defaults.CreateModuleDefinitions().FirstOrDefault(d => d.Id == moduleId)
                  ?? throw new InvalidOperationException($"未知模块：{moduleId}");
        if (!def.IsImplemented)
        {
            throw new InvalidOperationException("该模块在当前版本尚未实现，不能启用。");
        }

        if (def.IsCore)
        {
            throw new InvalidOperationException("核心模块不能停用。");
        }

        var settings = Load();
        var setting = settings.Modules.FirstOrDefault(m => m.ModuleId == moduleId);
        if (setting is null)
        {
            settings.Modules.Add(new ModuleSetting { ModuleId = moduleId, IsEnabled = enabled });
        }
        else
        {
            setting.IsEnabled = enabled;
        }

        Save(settings);
    }

    public void SetNavVisible(string moduleId, bool visible)
    {
        var settings = Load();
        var setting = settings.NavItems.FirstOrDefault(n => n.ModuleId == moduleId);
        if (setting is null)
        {
            settings.NavItems.Add(new NavItemSetting { ModuleId = moduleId, IsVisible = visible, Order = settings.NavItems.Count });
        }
        else
        {
            setting.IsVisible = visible;
        }

        Save(settings);
    }

    public void MoveNavItem(string moduleId, int delta)
    {
        var settings = Load();
        var items = settings.NavItems.OrderBy(n => n.Order).ToList();
        var index = items.FindIndex(n => n.ModuleId == moduleId);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= items.Count)
        {
            return;
        }

        (items[index], items[target]) = (items[target], items[index]);
        for (var i = 0; i < items.Count; i++)
        {
            items[i].Order = i;
        }

        settings.NavItems = items;
        Save(settings);
    }

    public IReadOnlyList<HomeCardDefinition> GetHomeCardDefinitions() => Defaults.CreateHomeCardDefinitions();

    public IReadOnlyList<HomeCardSetting> GetHomeCards() => Load().HomeCards.OrderBy(c => c.Order).ToList();

    public void SetHomeCardVisible(string cardId, bool visible)
    {
        var settings = Load();
        var card = settings.HomeCards.FirstOrDefault(c => c.CardId == cardId);
        if (card is null)
        {
            return;
        }

        card.IsVisible = visible;
        Save(settings);
    }

    public void MoveHomeCard(string cardId, int delta)
    {
        var settings = Load();
        var cards = settings.HomeCards.OrderBy(c => c.Order).ToList();
        var index = cards.FindIndex(c => c.CardId == cardId);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= cards.Count)
        {
            return;
        }

        (cards[index], cards[target]) = (cards[target], cards[index]);
        for (var i = 0; i < cards.Count; i++)
        {
            cards[i].Order = i;
        }

        settings.HomeCards = cards;
        Save(settings);
    }
}
