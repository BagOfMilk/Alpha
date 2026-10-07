using System;
using System.Collections.Generic;
using Game.Core.Characters;
using Game.Core.Characters.Creation;
using Game.Core.Items;
using Game.Core.Session;
using Game.Core.Session.Views;
using Game.Gameplay.Characters;
using Game.Gameplay.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.UI.Toolkit
{
    /// <summary>
    /// «Лялька» спорядження й кузня (Поправка №19.3; UI Toolkit за №17.4), клавіша I. Зверху — хто
    /// вдягається; ліворуч — вісім слотів (клік — показати речі для слота, «Зняти»); посередині — модель
    /// з надітим (тягнути мишею — поворот); праворуч — сховок («Надіти») і кузня Збройні з ціною до кліку
    /// (Статут UI-02). Правила — <see cref="InventoryModel"/> (чистий C#, під тестами); команди ядра —
    /// ті самі, що в панелі Складу. Щокадру екран перечитує сесію і перебудовує списки лише коли
    /// змінився їхній підпис — так не губляться наведення й прокрутка.
    /// </summary>
    public sealed class InventoryToolkitView : IShellOverlay
    {
        private readonly GameShell _shell;
        private GameObject _host;
        private PanelSettings _panel;
        private VisualElement _screen;
        private CharacterPreviewRig _preview;
        private bool _wasVisible;

        private Label _title, _dollCaption, _stashCaption, _forgeCaption, _wallet, _hint, _message;
        private VisualElement _people, _slots, _stash, _forge;
        private Button _close, _allItems;
        private Image _image;
        private bool _dragging;
        private float _lastX;

        private string _companionId;
        private EquipSlot? _filter;
        private string _peopleSig, _slotsSig, _stashSig, _forgeSig;
        private string _forgeMessage;

        private InventoryToolkitView(GameShell shell)
        {
            _shell = shell;
        }

        /// <summary>Міст для <see cref="GameShell"/> (рефлексія): null — «ляльки» немає, лишається панель Складу.</summary>
        public static IShellOverlay TryCreate(GameShell shell)
        {
            var view = new InventoryToolkitView(shell);
            try
            {
                view.Build();
                return view;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Спорядження] UI Toolkit недоступний: " + ex.Message);
                view.Dispose();
                return null;
            }
        }

        /// <summary>Стан-екрани не підміняє — «лялька» живе поверх села.</summary>
        public bool Handles(SessionState state) => false;

        // ============================ побудова ============================

        private void Build()
        {
            _preview = UnityEngine.Object.FindFirstObjectByType<CharacterPreviewRig>();
            if (_preview == null || _preview.Library == null || !_preview.Library.IsComplete)
                throw new InvalidOperationException("немає CharacterPreviewRig з набором");

            var asset = Resources.Load<PanelSettings>(HudToolkitTheme.PanelResourcePath);
            _panel = asset != null ? UnityEngine.Object.Instantiate(asset) : ScriptableObject.CreateInstance<PanelSettings>();
            _panel.name = "AlphaInventoryPanel";
            _panel.scaleMode = PanelScaleMode.ConstantPixelSize;
            _panel.scale = HudLayout.ScaleFor(Screen.width, Screen.height);
            _panel.sortingOrder = 2f; // поверх шапки HUD
            if (_panel.themeStyleSheet == null)
            {
                var theme = Resources.Load<ThemeStyleSheet>(HudToolkitTheme.ThemeResourcePath);
                if (theme != null) _panel.themeStyleSheet = theme;
            }

            _host = new GameObject("AlphaInventory (UI Toolkit)");
            _host.SetActive(false);
            var document = _host.AddComponent<UIDocument>();
            document.panelSettings = _panel;
            _host.SetActive(true);
            var root = document.rootVisualElement;
            if (root == null) throw new InvalidOperationException("UIDocument.rootVisualElement == null");

            _screen = new VisualElement { name = "inventory" };
            _screen.style.position = Position.Absolute;
            _screen.style.left = 0f; _screen.style.top = 0f; _screen.style.right = 0f; _screen.style.bottom = 0f;
            _screen.style.backgroundColor = new Color(0.06f, 0.055f, 0.05f, 0.97f);
            _screen.style.flexDirection = FlexDirection.Column;
            HudToolkitTheme.Padding(_screen, 16f, 22f);
            var font = HudToolkitTheme.LoadFont();
            if (font != null) _screen.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(font));
            _screen.style.fontSize = HudToolkitTheme.BodySize;
            _screen.style.color = HudToolkitTheme.TextColor;
            _screen.style.display = DisplayStyle.None;
            root.Add(_screen);

            var top = Row();
            _title = HudToolkitTheme.Text(string.Empty, HudToolkitTheme.TitleSize + 6f, HudToolkitTheme.AccentColor, bold: true);
            _title.style.marginRight = 18f;
            top.Add(_title);
            _people = Row();
            _people.style.flexGrow = 1f;
            _people.style.flexWrap = Wrap.Wrap;
            top.Add(_people);
            _close = Ui.MakeButton(string.Empty, () => _shell.InventoryOpen = false);
            top.Add(_close);
            _screen.Add(top);

            var columns = Row();
            columns.style.flexGrow = 1f;
            columns.style.alignItems = Align.Stretch;
            columns.style.marginTop = 10f;
            _screen.Add(columns);

            var left = Ui.Column(360f, false);
            _dollCaption = Ui.Caption(left);
            var slotScroll = new ScrollView(ScrollViewMode.Vertical);
            slotScroll.style.flexGrow = 1f;
            _slots = slotScroll.contentContainer;
            left.Add(slotScroll);
            columns.Add(left);

            var center = Ui.Column(0f, true);
            center.style.alignItems = Align.Center;
            _image = new Image { name = "inventory-preview", scaleMode = ScaleMode.ScaleToFit };
            _image.style.flexGrow = 1f;
            _image.style.alignSelf = Align.Stretch;
            _image.RegisterCallback<PointerDownEvent>(e => { _dragging = true; _lastX = e.position.x; _image.CapturePointer(e.pointerId); });
            _image.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_dragging) return;
                _preview.Yaw += (e.position.x - _lastX) * 0.6f;
                _lastX = e.position.x;
            });
            _image.RegisterCallback<PointerUpEvent>(e => { _dragging = false; _image.ReleasePointer(e.pointerId); });
            center.Add(_image);
            _hint = HudToolkitTheme.Text(string.Empty, HudToolkitTheme.BodySize - 2f, HudToolkitTheme.TextDimColor);
            center.Add(_hint);
            columns.Add(center);

            var right = Ui.Column(440f, false);
            right.style.marginRight = 0f;
            var stashHead = Row();
            _stashCaption = Ui.Caption(stashHead);
            _stashCaption.style.flexGrow = 1f;
            _allItems = Ui.MakeButton(string.Empty, () => _filter = null);
            stashHead.Add(_allItems);
            right.Add(stashHead);
            var stashScroll = new ScrollView(ScrollViewMode.Vertical);
            stashScroll.style.flexGrow = 1f;
            stashScroll.style.minHeight = 160f;
            _stash = stashScroll.contentContainer;
            right.Add(stashScroll);
            _forgeCaption = Ui.Caption(right);
            _wallet = HudToolkitTheme.Text(string.Empty, HudToolkitTheme.BodySize - 2f, HudToolkitTheme.TextDimColor);
            right.Add(_wallet);
            var forgeScroll = new ScrollView(ScrollViewMode.Vertical);
            forgeScroll.style.flexGrow = 1f;
            forgeScroll.style.minHeight = 160f;
            _forge = forgeScroll.contentContainer;
            right.Add(forgeScroll);
            columns.Add(right);

            _message = HudToolkitTheme.Text(string.Empty, HudToolkitTheme.BodySize, HudToolkitTheme.WarningColor);
            _message.style.marginTop = 8f;
            _screen.Add(_message);
        }

        private static VisualElement Row()
        {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row;
            r.style.alignItems = Align.Center;
            return r;
        }

        // ============================ кожен кадр ============================

        public void Tick()
        {
            if (_screen == null) return;
            var session = _shell.Session;
            bool visible = _shell.InventoryOpen && session != null && _shell.CanExplore;
            if (_shell.InventoryOpen && !visible) _shell.InventoryOpen = false;
            _screen.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (visible != _wasVisible)
            {
                _preview.SetActive(visible);
                _wasVisible = visible;
                _peopleSig = _slotsSig = _stashSig = _forgeSig = null;
                _forgeMessage = null;
            }
            if (!visible) return;

            var g = _shell.ProtagonistGender;
            var roster = session.GetRosterView();
            var wearers = InventoryModel.Wearers(roster);
            if (wearers.Count == 0) { _shell.InventoryOpen = false; return; }
            if (_companionId == null || !wearers.Contains(_companionId)) _companionId = wearers[0];

            var sheet = session.GetCharacterSheet(_companionId);
            var equipment = sheet != null ? sheet.Equipment : null;
            var stash = session.GetStash();
            var offers = session.GetForgeOffers();
            var economy = session.GetEconomyView();

            _title.text = UkrainianText.Get("ui.inventory.title", g);
            _close.text = UkrainianText.Get("ui.inventory.close", g);
            _dollCaption.text = UkrainianText.Get("ui.inventory.doll", g);
            _stashCaption.text = _filter == null
                ? UkrainianText.Get("ui.inventory.stash", g)
                : UkrainianText.Get("ui.inventory.stash", g) + " · " + UkrainianText.Get(InventoryModel.SlotKey(_filter.Value), g);
            _allItems.text = UkrainianText.Get("ui.inventory.stash.all", g);
            _allItems.style.display = _filter == null ? DisplayStyle.None : DisplayStyle.Flex;
            _forgeCaption.text = UkrainianText.Get("ui.gear.forge.title", g);
            _wallet.text = economy != null
                ? UkrainianText.Get("resource.gold", g) + ": " + economy.Gold + " · " + UkrainianText.Get("resource.craft_component", g) + ": " + economy.CraftComponent
                : string.Empty;
            _hint.text = UkrainianText.Get("ui.inventory.hint", g);
            _message.text = !string.IsNullOrEmpty(_forgeMessage) ? _forgeMessage : (_shell.LastMessage ?? string.Empty);

            RefreshPeople(wearers, roster, g);
            RefreshSlots(equipment, g);
            RefreshStash(stash, roster, g);
            RefreshForge(offers, g);

            var look = session.GetAppearance(_companionId);
            if (look != null) _preview.Show(CharacterKitPlan.From(look, InventoryModel.VisualKeys(equipment)));
            if (_image.image != _preview.Texture) _image.image = _preview.Texture;
        }

        private void RefreshPeople(List<string> wearers, RosterView roster, Gender g)
        {
            string sig = string.Join(",", wearers.ToArray()) + "|" + _companionId;
            if (sig == _peopleSig) return;
            _peopleSig = sig;
            _people.Clear();
            foreach (var id in wearers)
            {
                string captured = id;
                var b = Ui.MakeButton(UxBricks.Name(_shell, id, roster), () => { _companionId = captured; _filter = null; });
                Ui.Mark(b, id == _companionId);
                b.style.marginRight = 6f;
                _people.Add(b);
            }
        }

        private void RefreshSlots(EquipmentSheetView equipment, Gender g)
        {
            var parts = new List<string>();
            foreach (var slot in InventoryModel.DollOrder)
            {
                var v = InventoryModel.SlotView(equipment, slot);
                parts.Add(slot + "=" + v.ItemId + (v.BlockedByTwoHanded ? "!" : ""));
            }
            string sig = _companionId + "|" + string.Join(",", parts.ToArray()) + "|" + _filter;
            if (sig == _slotsSig) return;
            _slotsSig = sig;
            _slots.Clear();
            foreach (var slot in InventoryModel.DollOrder)
            {
                var v = InventoryModel.SlotView(equipment, slot);
                var captured = slot;
                var card = new VisualElement();
                card.style.flexDirection = FlexDirection.Row;
                card.style.alignItems = Align.Center;
                card.style.marginBottom = 6f;
                card.style.backgroundColor = HudToolkitTheme.RaisedColor;
                HudToolkitTheme.Border(card, 1f, _filter == slot ? HudToolkitTheme.AccentColor : HudToolkitTheme.BorderColor);
                HudToolkitTheme.Padding(card, 6f, 10f);
                card.RegisterCallback<ClickEvent>(e => _filter = captured);

                var text = new VisualElement();
                text.style.flexGrow = 1f;
                text.Add(HudToolkitTheme.Text(UkrainianText.Get(InventoryModel.SlotKey(slot), g), HudToolkitTheme.BodySize - 2f, HudToolkitTheme.TextDimColor));
                string value = !string.IsNullOrEmpty(v.ItemId) ? Ui.ItemName(v.ItemId, g)
                    : v.BlockedByTwoHanded ? UkrainianText.Get("ui.inventory.blocked", g)
                    : UkrainianText.Get("ui.inventory.empty_slot", g);
                text.Add(HudToolkitTheme.Text(value, HudToolkitTheme.BodySize,
                    string.IsNullOrEmpty(v.ItemId) ? HudToolkitTheme.TextDimColor : HudToolkitTheme.TextColor));
                card.Add(text);
                if (!string.IsNullOrEmpty(v.ItemId))
                {
                    string companion = _companionId;
                    card.Add(Ui.MakeButton(UkrainianText.Get("ui.inventory.unequip", g),
                        () => _shell.TryRun(() => _shell.Session.Unequip(companion, captured))));
                }
                _slots.Add(card);
            }
        }

        private void RefreshStash(IReadOnlyList<ItemInstance> stash, RosterView roster, Gender g)
        {
            var items = InventoryModel.StashFor(stash, _filter);
            var ids = new List<string>();
            foreach (var i in items) ids.Add(i.InstanceId + ":" + i.Rarity);
            string sig = _companionId + "|" + _filter + "|" + string.Join(",", ids.ToArray());
            if (sig == _stashSig) return;
            _stashSig = sig;
            _stash.Clear();
            if (items.Count == 0)
            {
                _stash.Add(HudToolkitTheme.Text(UkrainianText.Get(_filter == null ? "ui.gear.stash.empty" : "ui.inventory.stash.empty", g),
                    HudToolkitTheme.BodySize, HudToolkitTheme.TextDimColor));
                return;
            }
            foreach (var item in items)
            {
                string instanceId = item.InstanceId;
                var slot = item.Slot;
                string companion = _companionId;
                var card = Ui.Card();
                var text = new VisualElement();
                text.style.flexGrow = 1f;
                text.Add(HudToolkitTheme.Text(Ui.ItemName(item.Definition.Id, g), HudToolkitTheme.BodySize, HudToolkitTheme.TextColor, bold: true));
                string meta = UkrainianText.Get(InventoryModel.SlotKey(slot), g) + " · "
                    + UkrainianText.Get("rarity." + item.Rarity.ToString().ToLowerInvariant(), g)
                    + (item.Definition.TwoHanded ? " · " + UkrainianText.Get("ui.inventory.two_handed", g) : string.Empty);
                text.Add(HudToolkitTheme.Text(meta, HudToolkitTheme.BodySize - 2f, HudToolkitTheme.TextDimColor));
                text.Add(HudToolkitTheme.Text(ScreenText.ItemStatSummary(item, g), HudToolkitTheme.BodySize - 2f, HudToolkitTheme.TextDimColor));
                card.Add(text);
                card.Add(Ui.MakeButton(UkrainianText.Get("ui.inventory.equip", g),
                    () => _shell.TryRun(() => _shell.Session.Equip(companion, instanceId, slot)), primary: true));
                _stash.Add(card);
            }
        }

        private void RefreshForge(IReadOnlyList<ForgeOfferView> offers, Gender g)
        {
            var list = InventoryModel.ForgeFor(offers, _filter);
            var parts = new List<string>();
            foreach (var o in list) parts.Add(o.ItemId + (o.Affordable ? "+" : "-") + (o.ArmoryOpen ? "o" : "c"));
            string sig = _filter + "|" + string.Join(",", parts.ToArray());
            if (sig == _forgeSig) return;
            _forgeSig = sig;
            _forge.Clear();
            if (list.Count > 0 && !list[0].ArmoryOpen)
                _forge.Add(HudToolkitTheme.Text(UkrainianText.Get("ui.gear.forge.closed", g), HudToolkitTheme.BodySize, HudToolkitTheme.WarningColor));
            foreach (var o in list)
            {
                string itemId = o.ItemId;
                var card = Ui.Card();
                var text = new VisualElement();
                text.style.flexGrow = 1f;
                text.Add(HudToolkitTheme.Text(Ui.ItemName(o.ItemId, g), HudToolkitTheme.BodySize, HudToolkitTheme.TextColor, bold: true));
                text.Add(HudToolkitTheme.Text(UkrainianText.Get(InventoryModel.SlotKey(o.Slot), g) + " · "
                    + UkrainianText.Format("ui.gear.forge.cost", g, "gold", o.GoldCost.ToString(), "craft", o.CraftCost.ToString()),
                    HudToolkitTheme.BodySize - 2f, o.Affordable ? HudToolkitTheme.TextDimColor : HudToolkitTheme.WarningColor));
                card.Add(text);
                var b = Ui.MakeButton(UkrainianText.Get("ui.gear.forge", g), () => Forge(itemId, g), primary: true);
                b.SetEnabled(o.Affordable);
                card.Add(b);
                _forge.Add(card);
            }
        }

        private void Forge(string itemId, Gender g)
        {
            var result = ForgeResult.UnknownItem;
            _shell.TryRun(() => result = _shell.Session.ForgeItem(itemId));
            string fail = InventoryModel.ForgeFailureKey(result);
            _forgeMessage = fail != null
                ? UkrainianText.Get(fail, g)
                : UkrainianText.Format("ui.gear.forge.made", g, "item", Ui.ItemName(itemId, g));
            _stashSig = _forgeSig = null;
        }

        public void Dispose()
        {
            if (_host != null) UnityEngine.Object.Destroy(_host);
            if (_panel != null) UnityEngine.Object.Destroy(_panel);
            _host = null;
            _panel = null;
            _screen = null;
        }

        // ============================ дрібні цеглини ============================

        private static class Ui
        {
            public static VisualElement Column(float width, bool grow)
            {
                var c = new VisualElement();
                c.style.flexDirection = FlexDirection.Column;
                if (grow) c.style.flexGrow = 1f; else c.style.width = width;
                c.style.backgroundColor = HudToolkitTheme.PanelColor;
                HudToolkitTheme.Border(c, 1f, HudToolkitTheme.BorderColor);
                HudToolkitTheme.Padding(c, 12f, 14f);
                c.style.marginRight = 12f;
                return c;
            }

            public static VisualElement Card()
            {
                var card = new VisualElement();
                card.style.flexDirection = FlexDirection.Row;
                card.style.alignItems = Align.Center;
                card.style.marginBottom = 6f;
                card.style.backgroundColor = HudToolkitTheme.RaisedColor;
                HudToolkitTheme.Border(card, 1f, HudToolkitTheme.BorderColor);
                HudToolkitTheme.Padding(card, 6f, 10f);
                return card;
            }

            public static Label Caption(VisualElement parent)
            {
                var l = HudToolkitTheme.Text(string.Empty, HudToolkitTheme.ZoneTitleSize, HudToolkitTheme.AccentColor, bold: true);
                l.style.marginTop = 6f;
                l.style.marginBottom = 4f;
                parent.Add(l);
                return l;
            }

            public static Button MakeButton(string text, Action onClick, bool primary = false)
            {
                var b = new Button(() => { SoundSettings.Request(SoundCue.UiClick); onClick(); }) { text = text };
                b.focusable = false;
                b.style.fontSize = HudToolkitTheme.BodySize;
                b.style.color = primary ? HudToolkitTheme.AccentColor : HudToolkitTheme.TextColor;
                var idle = HudToolkitTheme.RaisedColor;
                b.style.backgroundColor = idle;
                HudToolkitTheme.Padding(b, 5f, 12f);
                HudToolkitTheme.Border(b, 1f, primary ? HudToolkitTheme.AccentColor : HudToolkitTheme.BorderColor);
                b.RegisterCallback<PointerEnterEvent>(e => b.style.backgroundColor = HudToolkitTheme.HoverColor);
                b.RegisterCallback<PointerLeaveEvent>(e => b.style.backgroundColor = idle);
                return b;
            }

            public static void Mark(Button b, bool selected)
            {
                var c = selected ? HudToolkitTheme.AccentColor : HudToolkitTheme.BorderColor;
                b.style.borderTopColor = c; b.style.borderBottomColor = c; b.style.borderLeftColor = c; b.style.borderRightColor = c;
                b.style.unityFontStyleAndWeight = selected ? FontStyle.Bold : FontStyle.Normal;
            }

            public static string ItemName(string itemId, Gender g) =>
                UkrainianText.Has("item." + itemId, g) ? UkrainianText.Get("item." + itemId, g) : itemId;
        }
    }
}
