using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PKHeX.Core;

namespace PKLumiHex.Mac;

public partial class MainWindow : Window
{
    private const int CharizarditeX = 660;
    private const int CharizarditeY = 678;
    private const int KeyStone = 773;

    private SaveFile? _sav;
    private string? _savePath;
    private int _selectedSlot = -1;
    private bool _isPartySelected = true;
    private int _selectedBox;

    public MainWindow()
    {
        InitializeComponent();
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
            StatusText.Text = $"Loaded {Path.GetFileName(path)} ({sav.Version}, Gen {sav.Generation}, OT: {sav.OT})";
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
            StatusText.Text = $"Saved to {Path.GetFileName(_savePath)} (backup: {Path.GetFileName(backupPath)})";
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

        var containers = new List<string> { "Party" };
        for (int b = 0; b < _sav.BoxCount; b++)
            containers.Add($"Box {b + 1}");

        ContainerPicker.ItemsSource = containers;
        ContainerPicker.SelectedIndex = 0;

        var pockets = _sav.Inventory.Select((p, idx) => $"{idx}: {p.Type}").ToList();
        PocketPicker.ItemsSource = pockets;
        if (pockets.Count > 0)
            PocketPicker.SelectedIndex = 0;

        TrainerNameBox.Text = _sav.OT;
        MoneyBox.Text = _sav.Money.ToString();
        LanguageBox.Text = _sav.Language.ToString();
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
            return $"[{index + 1:00}] (Empty)";

        var speciesName = GetSpeciesName(pk.Species);
        var itemSuffix = pk.HeldItem > 0 ? $" @ {GetItemName(pk.HeldItem)}" : string.Empty;
        var formSuffix = pk.Form > 0 ? $"-F{pk.Form}" : string.Empty;
        return $"[{index + 1:00}] {speciesName}{formSuffix} Lv.{pk.CurrentLevel}{itemSuffix}";
    }

    private void OnSlotSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_sav is null || SlotList.SelectedIndex < 0)
            return;

        _selectedSlot = SlotList.SelectedIndex;
        var pk = _isPartySelected
            ? _sav.GetPartySlotAtIndex(_selectedSlot)
            : _sav.GetBoxSlotAtIndex(_selectedBox, _selectedSlot);

        SlotHeader.Text = _isPartySelected
            ? $"Party Slot {_selectedSlot + 1} — {GetSpeciesName(pk.Species)}"
            : $"Box {_selectedBox + 1} Slot {_selectedSlot + 1} — {GetSpeciesName(pk.Species)}";

        NicknameBox.Text = pk.Nickname;
        SpeciesBox.Text = pk.Species.ToString();
        FormBox.Text = pk.Form.ToString();
        LevelBox.Text = pk.CurrentLevel.ToString();
        HeldItemBox.Text = pk.HeldItem.ToString();
        HeldItemNameText.Text = GetItemName(pk.HeldItem);
        ShinyCheck.IsChecked = pk.IsShiny;
        AbilityBox.Text = pk.Ability.ToString();

        Move1Box.Text = pk.Move1.ToString();
        Move2Box.Text = pk.Move2.ToString();
        Move3Box.Text = pk.Move3.ToString();
        Move4Box.Text = pk.Move4.ToString();

        IvHpBox.Text = pk.IV_HP.ToString();
        IvAtkBox.Text = pk.IV_ATK.ToString();
        IvDefBox.Text = pk.IV_DEF.ToString();
        IvSpaBox.Text = pk.IV_SPA.ToString();
        IvSpdBox.Text = pk.IV_SPD.ToString();
        IvSpeBox.Text = pk.IV_SPE.ToString();

        EvHpBox.Text = pk.EV_HP.ToString();
        EvAtkBox.Text = pk.EV_ATK.ToString();
        EvDefBox.Text = pk.EV_DEF.ToString();
        EvSpaBox.Text = pk.EV_SPA.ToString();
        EvSpdBox.Text = pk.EV_SPD.ToString();
        EvSpeBox.Text = pk.EV_SPE.ToString();
        PokemonStatusText.Text = string.Empty;
    }

    private void OnGiveCharizarditeX(object? sender, RoutedEventArgs e)
    {
        HeldItemBox.Text = CharizarditeX.ToString();
        HeldItemNameText.Text = GetItemName(CharizarditeX);
    }

    private void OnGiveCharizarditeY(object? sender, RoutedEventArgs e)
    {
        HeldItemBox.Text = CharizarditeY.ToString();
        HeldItemNameText.Text = GetItemName(CharizarditeY);
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

            if (ushort.TryParse(SpeciesBox.Text, out var species))
                pk.Species = species;
            if (byte.TryParse(FormBox.Text, out var form))
                pk.Form = form;
            if (int.TryParse(LevelBox.Text, out var level))
                pk.CurrentLevel = Math.Clamp(level, 1, 100);
            if (int.TryParse(HeldItemBox.Text, out var heldItem))
                pk.HeldItem = Math.Max(0, heldItem);
            if (!string.IsNullOrWhiteSpace(NicknameBox.Text))
                pk.Nickname = NicknameBox.Text.Trim();
            if (int.TryParse(AbilityBox.Text, out var ability))
                pk.Ability = Math.Max(0, ability);

            pk.SetIsShiny(ShinyCheck.IsChecked == true);

            if (ushort.TryParse(Move1Box.Text, out var m1)) pk.Move1 = m1;
            if (ushort.TryParse(Move2Box.Text, out var m2)) pk.Move2 = m2;
            if (ushort.TryParse(Move3Box.Text, out var m3)) pk.Move3 = m3;
            if (ushort.TryParse(Move4Box.Text, out var m4)) pk.Move4 = m4;
            pk.HealPP();

            if (int.TryParse(IvHpBox.Text, out var ivHp)) pk.IV_HP = Math.Clamp(ivHp, 0, 31);
            if (int.TryParse(IvAtkBox.Text, out var ivAtk)) pk.IV_ATK = Math.Clamp(ivAtk, 0, 31);
            if (int.TryParse(IvDefBox.Text, out var ivDef)) pk.IV_DEF = Math.Clamp(ivDef, 0, 31);
            if (int.TryParse(IvSpaBox.Text, out var ivSpa)) pk.IV_SPA = Math.Clamp(ivSpa, 0, 31);
            if (int.TryParse(IvSpdBox.Text, out var ivSpd)) pk.IV_SPD = Math.Clamp(ivSpd, 0, 31);
            if (int.TryParse(IvSpeBox.Text, out var ivSpe)) pk.IV_SPE = Math.Clamp(ivSpe, 0, 31);

            if (int.TryParse(EvHpBox.Text, out var evHp)) pk.EV_HP = Math.Clamp(evHp, 0, 252);
            if (int.TryParse(EvAtkBox.Text, out var evAtk)) pk.EV_ATK = Math.Clamp(evAtk, 0, 252);
            if (int.TryParse(EvDefBox.Text, out var evDef)) pk.EV_DEF = Math.Clamp(evDef, 0, 252);
            if (int.TryParse(EvSpaBox.Text, out var evSpa)) pk.EV_SPA = Math.Clamp(evSpa, 0, 252);
            if (int.TryParse(EvSpdBox.Text, out var evSpd)) pk.EV_SPD = Math.Clamp(evSpd, 0, 252);
            if (int.TryParse(EvSpeBox.Text, out var evSpe)) pk.EV_SPE = Math.Clamp(evSpe, 0, 252);

            pk.RefreshChecksum();

            if (_isPartySelected)
                _sav.SetPartySlotAtIndex(pk, _selectedSlot);
            else
                _sav.SetBoxSlotAtIndex(pk, _selectedBox, _selectedSlot);

            int keepSlot = _selectedSlot;
            RefreshSlotList();
            SlotList.SelectedIndex = keepSlot;
            PokemonStatusText.Text = "Slot updated (remember to Save).";
        }
        catch (Exception ex)
        {
            PokemonStatusText.Text = $"Update failed: {ex.Message}";
        }
    }

    private void OnPocketChanged(object? sender, SelectionChangedEventArgs e) => RefreshBagList();

    private void RefreshBagList()
    {
        if (_sav is null || PocketPicker.SelectedIndex < 0)
            return;

        var pouch = _sav.Inventory[PocketPicker.SelectedIndex];
        var rows = new List<string>();
        foreach (var item in pouch.Items)
        {
            if (item.Index > 0 && item.Count > 0)
                rows.Add($"#{item.Index} {GetItemName(item.Index)} x{item.Count}");
        }
        BagItemList.ItemsSource = rows;
    }

    private void OnBagItemSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (BagItemList.SelectedItem is not string text || !text.StartsWith('#'))
            return;

        var parts = text.Split(' ');
        if (parts.Length > 0 && int.TryParse(parts[0].TrimStart('#'), out var id))
            BagItemIdBox.Text = id.ToString();
    }

    private void OnAddBagItemClick(object? sender, RoutedEventArgs e)
    {
        if (_sav is null || PocketPicker.SelectedIndex < 0)
            return;

        if (!int.TryParse(BagItemIdBox.Text, out var itemId) || itemId <= 0 ||
            !int.TryParse(BagItemCountBox.Text, out var count) || count < 0)
        {
            BagStatusText.Text = "Enter a valid Item ID and Count.";
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
        BagStatusText.Text = $"Set #{itemId} ({GetItemName(itemId)}) x{count} (remember to Save).";
    }

    private void OnGrantMegaKitClick(object? sender, RoutedEventArgs e)
    {
        if (_sav is null)
            return;

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
        UpsertItemInPouch(keyPouch, KeyStone, 1);
        _sav.Inventory = pouches;

        RefreshBagList();
        BagStatusText.Text = "Added Key Stone (773), Charizardite X (660), and Charizardite Y (678).";
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
        if (uint.TryParse(MoneyBox.Text, out var money))
            _sav.Money = money;
        if (int.TryParse(LanguageBox.Text, out var lang))
            _sav.Language = lang;

        TrainerStatusText.Text = "Trainer info updated (remember to Save).";
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
