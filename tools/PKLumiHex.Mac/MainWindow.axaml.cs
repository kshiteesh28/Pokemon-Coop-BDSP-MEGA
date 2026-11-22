using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PKHeX.Core;

namespace PKLumiHex.Mac;

public partial class MainWindow : Window
{
    private const int CharizarditeX = 660;
    private const int CharizarditeY = 678;
    private const int MegaRing = 696;
    private const int MegaBracelet = 744;
    private const int KeyStone = 773;

    private SaveFile? _sav;
    private string? _savePath;
    private int _selectedSlot = -1;
    private bool _isPartySelected = true;
    private int _selectedBox;

    private List<string> _speciesChoices = new();
    private List<string> _itemChoices = new();
    private List<string> _moveChoices = new();
    private List<string> _abilityChoices = new();

    public MainWindow()
    {
        InitializeComponent();
        PopulateLookupLists();
    }

    private void PopulateLookupLists()
    {
        _speciesChoices = BuildChoiceList(GameInfo.Strings.Species);
        _itemChoices = BuildChoiceList(GameInfo.Strings.Item);
        _moveChoices = BuildChoiceList(GameInfo.Strings.Move);
        _abilityChoices = BuildChoiceList(GameInfo.Strings.Ability);

        SpeciesAutoBox.ItemsSource = _speciesChoices;
        HeldItemAutoBox.ItemsSource = _itemChoices;
        BagItemAutoBox.ItemsSource = _itemChoices;
        AbilityAutoBox.ItemsSource = _abilityChoices;

        Move1AutoBox.ItemsSource = _moveChoices;
        Move2AutoBox.ItemsSource = _moveChoices;
        Move3AutoBox.ItemsSource = _moveChoices;
        Move4AutoBox.ItemsSource = _moveChoices;
    }

    private static List<string> BuildChoiceList(IReadOnlyList<string> source)
    {
        var list = new List<string>(source.Count);
        for (int i = 0; i < source.Count; i++)
        {
            var name = string.IsNullOrWhiteSpace(source[i]) ? $"#{i}" : source[i];
            list.Add($"{name} ({i})");
        }
        return list;
    }

    private static string FormatChoice(IReadOnlyList<string> source, int id)
    {
        if (id >= 0 && id < source.Count && !string.IsNullOrWhiteSpace(source[id]))
            return $"{source[id]} ({id})";
        return $"#{id} ({id})";
    }

    private static bool TryParseChoiceId(string? text, IReadOnlyList<string> source, out int id)
    {
        id = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        if (int.TryParse(trimmed, out id))
            return true;

        int open = trimmed.LastIndexOf('(');
        int close = trimmed.LastIndexOf(')');
        if (open >= 0 && close > open + 1 && int.TryParse(trimmed.Substring(open + 1, close - open - 1), out id))
            return true;

        for (int i = 0; i < source.Count; i++)
        {
            if (string.Equals(source[i], trimmed, StringComparison.OrdinalIgnoreCase))
            {
                id = i;
                return true;
            }
        }
        return false;
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Luminescent Platinum Save File",
            AllowMultiple = false
        });
        if (files.Count == 0)
            return;

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            var data = await File.ReadAllBytesAsync(path);
            var sav = SaveUtil.GetVariantSAV(data);
            if (sav is null)
            {
                StatusText.Text = "Unrecognized save file format.";
                return;
            }

            _sav = sav;
            _savePath = path;
            SaveButton.IsEnabled = true;
            SaveAsButton.IsEnabled = true;
            MainTabs.IsEnabled = true;

            LoadSaveIntoUi();
            StatusText.Text = $"{Path.GetFileName(path)}  •  {sav.Version} (Gen {sav.Generation})  •  OT: {sav.OT}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Open failed: {ex.Message}";
        }
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (_sav is null || string.IsNullOrEmpty(_savePath))
            return;

        try
        {
            var backupPath = $"{_savePath}.bak";
            if (File.Exists(_savePath))
                File.Copy(_savePath, backupPath, overwrite: true);

            var bytes = _sav.Write();
            await File.WriteAllBytesAsync(_savePath, bytes);
            StatusText.Text = $"Saved {Path.GetFileName(_savePath)} (backup saved to {Path.GetFileName(backupPath)})";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Save failed: {ex.Message}";
        }
    }

    private async void OnSaveAsClick(object? sender, RoutedEventArgs e)
    {
        if (_sav is null)
            return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Luminescent Platinum Save File",
            SuggestedFileName = string.IsNullOrEmpty(_savePath) ? "SaveData.bin" : Path.GetFileName(_savePath)
        });
        var path = file?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            var bytes = _sav.Write();
            await File.WriteAllBytesAsync(path, bytes);
            _savePath = path;
            StatusText.Text = $"Saved as {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Save As failed: {ex.Message}";
        }
    }

    private void LoadSaveIntoUi()
    {
        if (_sav is null)
            return;

        var containers = new List<string> { "Party (6)" };
        for (int b = 0; b < _sav.BoxCount; b++)
            containers.Add($"Box {b + 1}");

        ContainerPicker.ItemsSource = containers;
        ContainerPicker.SelectedIndex = 0;

        var pockets = new List<string>();
        if (_sav is SAV8BS)
            pockets.Add("All Owned Items (BDSP Raw)");
        pockets.AddRange(_sav.Inventory.Select((p, idx) => $"{p.Type} ({idx})"));
        PocketPicker.ItemsSource = pockets;
        if (pockets.Count > 0)
            PocketPicker.SelectedIndex = 0;

        TrainerNameBox.Text = _sav.OT;
        MoneySpinner.Value = _sav.Money;
        LanguageSpinner.Value = _sav.Language;
        VersionText.Text = $"{_sav.Version} (Gen {_sav.Generation})";
    }

    private void OnContainerChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_sav is null || ContainerPicker.SelectedIndex < 0)
            return;

        _isPartySelected = ContainerPicker.SelectedIndex == 0;
        _selectedBox = Math.Max(0, ContainerPicker.SelectedIndex - 1);
        RefreshSlotList();
    }

    private void RefreshSlotList()
    {
        if (_sav is null)
            return;

        var items = new List<string>();
        int count = _isPartySelected ? 6 : _sav.BoxSlotCount;
        for (int i = 0; i < count; i++)
        {
            var pk = _isPartySelected
                ? _sav.GetPartySlotAtIndex(i)
                : _sav.GetBoxSlotAtIndex(_selectedBox, i);
            items.Add(FormatSlot(i, pk));
        }

        SlotList.ItemsSource = items;
        if (items.Count > 0)
            SlotList.SelectedIndex = 0;
    }

    private static string FormatSlot(int index, PKM pk)
    {
        if (pk.Species == 0)
            return $"{index + 1:00}  —  Empty";

        var speciesName = GetSpeciesName(pk.Species);
        var shiny = pk.IsShiny ? " ★" : string.Empty;
        var formSuffix = pk.Form > 0 ? $" (Form {pk.Form})" : string.Empty;
        var itemSuffix = pk.HeldItem > 0 ? $"  •  {GetItemName(pk.HeldItem)}" : string.Empty;
        return $"{index + 1:00}  {speciesName}{formSuffix}{shiny}  Lv.{pk.CurrentLevel}{itemSuffix}";
    }

    private void OnSlotSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_sav is null || SlotList.SelectedIndex < 0)
            return;

        _selectedSlot = SlotList.SelectedIndex;
        var pk = _isPartySelected
            ? _sav.GetPartySlotAtIndex(_selectedSlot)
            : _sav.GetBoxSlotAtIndex(_selectedBox, _selectedSlot);

        var location = _isPartySelected ? $"Party Slot {_selectedSlot + 1}" : $"Box {_selectedBox + 1}, Slot {_selectedSlot + 1}";
        SlotHeader.Text = pk.Species == 0 ? $"{location} — Empty" : $"{GetSpeciesName(pk.Species)} (Lv. {pk.CurrentLevel})";
        SlotSubheader.Text = pk.Species == 0
            ? "Pick a species below and click Apply Changes to create a Pokémon here."
            : $"{location}  •  Held: {(pk.HeldItem > 0 ? GetItemName(pk.HeldItem) : "None")}";

        SpeciesAutoBox.Text = FormatChoice(GameInfo.Strings.Species, pk.Species);
        NicknameBox.Text = pk.Nickname;
        HeldItemAutoBox.Text = FormatChoice(GameInfo.Strings.Item, pk.HeldItem);
        AbilityAutoBox.Text = FormatChoice(GameInfo.Strings.Ability, pk.Ability);
        LevelSpinner.Value = Math.Clamp(pk.CurrentLevel, 1, 100);
        FormSpinner.Value = pk.Form;
        ShinyCheck.IsChecked = pk.IsShiny;

        Move1AutoBox.Text = FormatChoice(GameInfo.Strings.Move, pk.Move1);
        Move2AutoBox.Text = FormatChoice(GameInfo.Strings.Move, pk.Move2);
        Move3AutoBox.Text = FormatChoice(GameInfo.Strings.Move, pk.Move3);
        Move4AutoBox.Text = FormatChoice(GameInfo.Strings.Move, pk.Move4);

        IvHpSpinner.Value = pk.IV_HP;
        IvAtkSpinner.Value = pk.IV_ATK;
        IvDefSpinner.Value = pk.IV_DEF;
        IvSpaSpinner.Value = pk.IV_SPA;
        IvSpdSpinner.Value = pk.IV_SPD;
        IvSpeSpinner.Value = pk.IV_SPE;

        EvHpSpinner.Value = pk.EV_HP;
        EvAtkSpinner.Value = pk.EV_ATK;
        EvDefSpinner.Value = pk.EV_DEF;
        EvSpaSpinner.Value = pk.EV_SPA;
        EvSpdSpinner.Value = pk.EV_SPD;
        EvSpeSpinner.Value = pk.EV_SPE;
        UpdateEvTotal();
        PokemonStatusText.Text = string.Empty;
    }

    private void OnGiveCharizarditeX(object? sender, RoutedEventArgs e)
    {
        HeldItemAutoBox.Text = FormatChoice(GameInfo.Strings.Item, CharizarditeX);
        PokemonStatusText.Text = "Selected Charizardite X — click Apply Changes to Slot.";
    }

    private void OnGiveCharizarditeY(object? sender, RoutedEventArgs e)
    {
        HeldItemAutoBox.Text = FormatChoice(GameInfo.Strings.Item, CharizarditeY);
        PokemonStatusText.Text = "Selected Charizardite Y — click Apply Changes to Slot.";
    }

    private void OnMaxIvsClick(object? sender, RoutedEventArgs e)
    {
        IvHpSpinner.Value = 31;
        IvAtkSpinner.Value = 31;
        IvDefSpinner.Value = 31;
        IvSpaSpinner.Value = 31;
        IvSpdSpinner.Value = 31;
        IvSpeSpinner.Value = 31;
    }

    private void OnEvChanged(object? sender, NumericUpDownValueChangedEventArgs e) => UpdateEvTotal();

    private void UpdateEvTotal()
    {
        int total =
            (int)(EvHpSpinner.Value ?? 0) +
            (int)(EvAtkSpinner.Value ?? 0) +
            (int)(EvDefSpinner.Value ?? 0) +
            (int)(EvSpaSpinner.Value ?? 0) +
            (int)(EvSpdSpinner.Value ?? 0) +
            (int)(EvSpeSpinner.Value ?? 0);
        EvTotalText.Text = $"EV Total: {total} / 510";
    }

    private void OnApplyPokemonClick(object? sender, RoutedEventArgs e)
    {
        if (_sav is null || _selectedSlot < 0)
            return;

        try
        {
            var pk = _isPartySelected
                ? _sav.GetPartySlotAtIndex(_selectedSlot)
                : _sav.GetBoxSlotAtIndex(_selectedBox, _selectedSlot);

            if (TryParseChoiceId(SpeciesAutoBox.Text, GameInfo.Strings.Species, out var species) && species >= 0)
                pk.Species = (ushort)species;
            if (TryParseChoiceId(HeldItemAutoBox.Text, GameInfo.Strings.Item, out var heldItem) && heldItem >= 0)
                pk.HeldItem = heldItem;
            if (TryParseChoiceId(AbilityAutoBox.Text, GameInfo.Strings.Ability, out var ability) && ability >= 0)
                pk.Ability = ability;

            pk.Form = (byte)Math.Clamp((int)(FormSpinner.Value ?? 0), 0, 64);
            pk.CurrentLevel = Math.Clamp((int)(LevelSpinner.Value ?? 1), 1, 100);
            if (!string.IsNullOrWhiteSpace(NicknameBox.Text))
                pk.Nickname = NicknameBox.Text.Trim();
            pk.SetIsShiny(ShinyCheck.IsChecked == true);

            if (TryParseChoiceId(Move1AutoBox.Text, GameInfo.Strings.Move, out var m1) && m1 >= 0) pk.Move1 = (ushort)m1;
            if (TryParseChoiceId(Move2AutoBox.Text, GameInfo.Strings.Move, out var m2) && m2 >= 0) pk.Move2 = (ushort)m2;
            if (TryParseChoiceId(Move3AutoBox.Text, GameInfo.Strings.Move, out var m3) && m3 >= 0) pk.Move3 = (ushort)m3;
            if (TryParseChoiceId(Move4AutoBox.Text, GameInfo.Strings.Move, out var m4) && m4 >= 0) pk.Move4 = (ushort)m4;
            pk.HealPP();

            pk.IV_HP = Math.Clamp((int)(IvHpSpinner.Value ?? 0), 0, 31);
            pk.IV_ATK = Math.Clamp((int)(IvAtkSpinner.Value ?? 0), 0, 31);
            pk.IV_DEF = Math.Clamp((int)(IvDefSpinner.Value ?? 0), 0, 31);
            pk.IV_SPA = Math.Clamp((int)(IvSpaSpinner.Value ?? 0), 0, 31);
            pk.IV_SPD = Math.Clamp((int)(IvSpdSpinner.Value ?? 0), 0, 31);
            pk.IV_SPE = Math.Clamp((int)(IvSpeSpinner.Value ?? 0), 0, 31);

            pk.EV_HP = Math.Clamp((int)(EvHpSpinner.Value ?? 0), 0, 252);
            pk.EV_ATK = Math.Clamp((int)(EvAtkSpinner.Value ?? 0), 0, 252);
            pk.EV_DEF = Math.Clamp((int)(EvDefSpinner.Value ?? 0), 0, 252);
            pk.EV_SPA = Math.Clamp((int)(EvSpaSpinner.Value ?? 0), 0, 252);
            pk.EV_SPD = Math.Clamp((int)(EvSpdSpinner.Value ?? 0), 0, 252);
            pk.EV_SPE = Math.Clamp((int)(EvSpeSpinner.Value ?? 0), 0, 252);

            pk.RefreshChecksum();

            if (_isPartySelected)
                _sav.SetPartySlotAtIndex(pk, _selectedSlot);
            else
                _sav.SetBoxSlotAtIndex(pk, _selectedBox, _selectedSlot);

            int keepSlot = _selectedSlot;
            RefreshSlotList();
            SlotList.SelectedIndex = keepSlot;
            PokemonStatusText.Text = "Applied! Click Save (+ .bak) in the top bar to write to disk.";
        }
        catch (Exception ex)
        {
            PokemonStatusText.Text = $"Update failed: {ex.Message}";
        }
    }

    private void OnPocketChanged(object? sender, SelectionChangedEventArgs e) => RefreshBagList();

    private void OnBagFilterChanged(object? sender, TextChangedEventArgs e) => RefreshBagList();

    private void RefreshBagList()
    {
        if (_sav is null || PocketPicker.SelectedIndex < 0)
            return;

        var filter = BagFilterBox.Text?.Trim() ?? string.Empty;
        var rows = new List<string>();

        if (_sav is SAV8BS sav8bs && PocketPicker.SelectedIndex == 0)
        {
            int maxId = Math.Min(sav8bs.MaxItemID, GameInfo.Strings.Item.Count - 1);
            for (int id = 1; id <= maxId; id++)
            {
                var entry = sav8bs.Items.GetItem(id);
                if (entry.Count <= 0)
                    continue;

                var label = $"{GetItemName(id)} ({id})  ×{entry.Count}";
                if (string.IsNullOrEmpty(filter) || label.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    rows.Add(label);
            }
            BagItemList.ItemsSource = rows;
            return;
        }

        int pouchIdx = _sav is SAV8BS ? PocketPicker.SelectedIndex - 1 : PocketPicker.SelectedIndex;
        if (pouchIdx < 0 || pouchIdx >= _sav.Inventory.Count)
            return;

        var pouch = _sav.Inventory[pouchIdx];
        foreach (var item in pouch.Items)
        {
            if (item.Index <= 0 || item.Count <= 0)
                continue;

            var label = $"{GetItemName(item.Index)} ({item.Index})  ×{item.Count}";
            if (string.IsNullOrEmpty(filter) || label.Contains(filter, StringComparison.OrdinalIgnoreCase))
                rows.Add(label);
        }
        BagItemList.ItemsSource = rows;
    }

    private void OnBagItemSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (BagItemList.SelectedItem is not string text)
            return;

        int timesIdx = text.LastIndexOf("×");
        if (timesIdx > 0 && int.TryParse(text.Substring(timesIdx + 1).Trim(), out var count))
            BagItemCountSpinner.Value = count;

        var itemPart = timesIdx > 0 ? text.Substring(0, timesIdx).Trim() : text;
        if (TryParseChoiceId(itemPart, GameInfo.Strings.Item, out var id))
            BagItemAutoBox.Text = FormatChoice(GameInfo.Strings.Item, id);
    }

    private void OnAddBagItemClick(object? sender, RoutedEventArgs e)
    {
        if (_sav is null || PocketPicker.SelectedIndex < 0)
            return;

        if (!TryParseChoiceId(BagItemAutoBox.Text, GameInfo.Strings.Item, out var itemId) || itemId <= 0)
        {
            BagStatusText.Text = "Pick a valid item name or ID.";
            return;
        }

        int count = Math.Max(0, (int)(BagItemCountSpinner.Value ?? 1));

        // For BDSP / Luminescent Platinum saves, write directly to MyItem8b so non-whitelisted
        // items (Mega Bracelet, Mega Ring, Key Stone, Charizardite X/Y) actually persist and display.
        if (_sav is SAV8BS sav8bs)
        {
            if (itemId > sav8bs.MaxItemID)
            {
                BagStatusText.Text = $"Item ID {itemId} exceeds save MaxItemID ({sav8bs.MaxItemID}).";
                return;
            }
            var entry = sav8bs.Items.GetItem(itemId);
            entry.Count = Math.Clamp(count, 0, 999);
            sav8bs.Items.SetItem(itemId, entry);
            if (PocketPicker.SelectedIndex != 0)
                PocketPicker.SelectedIndex = 0;
            RefreshBagList();
            BagStatusText.Text = $"Saved {GetItemName(itemId)} ({itemId}) ×{entry.Count} to BDSP Bag. Click Save (+ .bak)!";
            return;
        }

        var pouches = _sav.Inventory;
        var pouch = pouches[PocketPicker.SelectedIndex];
        if (!UpsertItemInPouch(pouch, itemId, count))
        {
            BagStatusText.Text = "Pocket is full.";
            return;
        }

        _sav.Inventory = pouches;
        RefreshBagList();
        BagStatusText.Text = $"Updated {GetItemName(itemId)} ({itemId}) ×{count}. Click Save to write.";
    }

    private void OnRemoveBagItemClick(object? sender, RoutedEventArgs e)
    {
        BagItemCountSpinner.Value = 0;
        OnAddBagItemClick(sender, e);
    }

    private void OnGrantMegaKitClick(object? sender, RoutedEventArgs e)
    {
        if (_sav is null)
            return;

        if (_sav is SAV8BS sav8bs)
        {
            foreach (var id in new[] { CharizarditeX, CharizarditeY, MegaBracelet, MegaRing, KeyStone })
            {
                if (id <= sav8bs.MaxItemID)
                {
                    var entry = sav8bs.Items.GetItem(id);
                    entry.Count = 1;
                    sav8bs.Items.SetItem(id, entry);
                }
            }
            PocketPicker.SelectedIndex = 0;
            RefreshBagList();
            BagStatusText.Text = "Added Mega Bracelet (744), Mega Ring (696), Key Stone (773), Charizardite X (660), and Charizardite Y (678). Click Save (+ .bak)!";
            return;
        }

        var pouches = _sav.Inventory;
        var itemsPouch = pouches.FirstOrDefault(p => p.Type == InventoryType.Items) ?? pouches.FirstOrDefault();
        var keyPouch = pouches.FirstOrDefault(p => p.Type == InventoryType.KeyItems) ?? itemsPouch;

        if (itemsPouch is null || keyPouch is null)
        {
            BagStatusText.Text = "Could not locate Bag pockets in this save.";
            return;
        }

        UpsertItemInPouch(itemsPouch, CharizarditeX, 1);
        UpsertItemInPouch(itemsPouch, CharizarditeY, 1);
        UpsertItemInPouch(keyPouch, MegaBracelet, 1);
        UpsertItemInPouch(keyPouch, MegaRing, 1);
        UpsertItemInPouch(keyPouch, KeyStone, 1);
        _sav.Inventory = pouches;

        RefreshBagList();
        BagStatusText.Text = "Added Mega Bracelet (744), Mega Ring (696), Key Stone (773), Charizardite X (660), and Charizardite Y (678). Click Save!";
    }

    private static bool UpsertItemInPouch(InventoryPouch pouch, int itemId, int count)
    {
        int clamped = Math.Clamp(count, 0, Math.Max(1, pouch.MaxCount));
        var existing = pouch.Items.FirstOrDefault(i => i.Index == itemId);
        if (existing is not null)
        {
            existing.Count = clamped;
            if (clamped == 0)
                existing.Index = 0;
            return true;
        }

        if (clamped == 0)
            return true;

        var empty = pouch.Items.FirstOrDefault(i => i.Index == 0 || i.Count == 0);
        if (empty is null)
            return false;

        empty.Index = itemId;
        empty.Count = clamped;
        return true;
    }

    private void OnApplyTrainerClick(object? sender, RoutedEventArgs e)
    {
        if (_sav is null)
            return;

        if (!string.IsNullOrWhiteSpace(TrainerNameBox.Text))
            _sav.OT = TrainerNameBox.Text.Trim();
        _sav.Money = (uint)Math.Clamp((long)(MoneySpinner.Value ?? 0), 0, 9999999);
        _sav.Language = Math.Clamp((int)(LanguageSpinner.Value ?? 2), 1, 10);

        TrainerStatusText.Text = "Trainer info applied! Click Save (+ .bak) to write.";
    }

    private static string GetSpeciesName(ushort species)
    {
        var names = GameInfo.Strings.Species;
        return species < names.Count ? names[species] : $"Species #{species}";
    }

    private static string GetItemName(int item)
    {
        var names = GameInfo.Strings.Item;
        return item >= 0 && item < names.Count ? names[item] : $"Item #{item}";
    }
}
