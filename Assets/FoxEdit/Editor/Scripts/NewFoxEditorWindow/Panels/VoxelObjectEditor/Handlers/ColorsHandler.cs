
using System;
using System.Linq;
using FoxEdit.WindowComponents;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FoxEdit.WindowPanels.VoxelObjectEditorPanelHandlers
{
    internal class ColorsHandler : baseVoxelObjectEditorPanelHandler
    {
        private DropdownField _paletteDropdown;
        private ColorPaletteElement _colorSelector;
        private Button _addPaletteButton;
        private Button _deletePaletteButton;
        private Button _renamePaletteButton;
        private TextField _renameField;
        private VisualElement _paletteSelectionContainer;

        private FoxEditSettings _foxEditSettings;

        public ColorsHandler(VisualElement root) : base(root)
        {
            _foxEditSettings = FoxEditSettings.GetSettings();
        }

        protected override void OnStartEditVoxelObject(VoxelObject voxelObject, VoxelRenderer voxelRenderer, VoxelEditor voxelEditor)
        {
            StopRename();
        }

        public override void GetElements()
        {
            _paletteDropdown = _root.Q<DropdownField>("palette-selector");
            _colorSelector = _root.Q<ColorPaletteElement>();
            _renameField = _root.Q<TextField>("palette-selection-rename-field");

            _paletteSelectionContainer = _root.Q<VisualElement>("palette-selection");
            _addPaletteButton = _paletteSelectionContainer.Q<Button>("add-button");
            _renamePaletteButton = _paletteSelectionContainer.Q<Button>("rename-button");
            _deletePaletteButton = _paletteSelectionContainer.Q<Button>("delete-button");
        }

        public override void RegisterCallbacks()
        {
            _paletteDropdown.RegisterValueChangedCallback<string>(OnPaletteValueChanged);
            _colorSelector.OnIndexChanged += OnColorSelectorValueChanged;
            _colorSelector.OnPressEditPaletteItem += OnPressEditPaletteItem;
            _colorSelector.OnPressDeletePaletteItem += OnPressDeletePaletteItem;
            _colorSelector.OnPressAddPaletteItem += OnPressAddPaletteItem;

            _addPaletteButton.clicked += OnPressAddPalette;
            _renamePaletteButton.clicked += OnPressRenamePalette;
            _deletePaletteButton.clicked += OnPressDeletePalette;

            VoxelEditor.OnChangeColor += OnChangeColor;
        }

        public override void UnregisterCallbacks()
        {
            _paletteDropdown.RegisterValueChangedCallback<string>(OnPaletteValueChanged);
            _colorSelector.OnIndexChanged -= OnColorSelectorValueChanged;
            _colorSelector.OnPressEditPaletteItem -= OnPressEditPaletteItem;
            _colorSelector.OnPressDeletePaletteItem -= OnPressDeletePaletteItem;

            _addPaletteButton.clicked -= OnPressAddPalette;
            _renamePaletteButton.clicked -= OnPressRenamePalette;
            _deletePaletteButton.clicked -= OnPressDeletePalette;

            VoxelEditor.OnChangeColor -= OnChangeColor;
        }

        public override void SetupFields()
        {
            _paletteDropdown.choices = VoxelSharedData.GetPaletteNames().ToList();
            _paletteDropdown.SetValueWithoutNotify(_paletteDropdown.choices[VoxelEditor.PaletteIndex]);
            UpdateColorSelector();
        }

        private void UpdateColorSelector()
        {
            VoxelPalette selectedPalette = VoxelEditor.CurrentPalette;
            _colorSelector.ClearPaletteItems();
            for (int i = 0; i < selectedPalette.Colors.Length; i++)
                _colorSelector.AddPaletteItem(selectedPalette.Colors[i]);
            _colorSelector.SetIndexValue(VoxelEditor.ColorIndex, false);
            _colorSelector.UpdatePaletteItemsManipulators();
        }

        private void RefreshPreviewColors()
        {
            VoxelSharedData.RefreshColorBuffer(VoxelEditor.PaletteIndex);
            _voxelEditor.RefreshPreviewColors(false);
            SceneView.RepaintAll();
        }

        private void OnPaletteValueChanged(ChangeEvent<string> evt)
        {
            int index = _paletteDropdown.choices.IndexOf(evt.newValue);

            if (index == -1)
                return;
            VoxelEditor.PaletteIndex = index;
            UpdateColorSelector();
            SceneView.RepaintAll();
        }

        private void OnPressAddPaletteItem()
        {
            ColorEditorPopUp.Open(ApplyAddColor, false, false);
        }

        private void ApplyAddColor(VoxelColor newVoxelColor)
        {
            VoxelEditor.CurrentPalette.AddColor(newVoxelColor);
            EditorUtility.SetDirty(VoxelEditor.CurrentPalette);
            RefreshPreviewColors();
            UpdateColorSelector();
        }

        private void OnPressDeletePaletteItem(int paletteItemIndex)
        {
            if (EditorUtility.DisplayDialog("Delete Color", "This will permanently remove the selected color from the palette. This action cannot be undone. Do you want to continue ?", "Delete", "Cancel"))
            {
                VoxelEditor.CurrentPalette.RemoveAt(paletteItemIndex);
                EditorUtility.SetDirty(VoxelEditor.CurrentPalette);
                RefreshPreviewColors();
            }
            UpdateColorSelector();
        }

        private void OnPressEditPaletteItem(int paletteItemIndex)
        {
            ColorEditorPopUp.Open((newColor) => ApplyEditPaletteItem(paletteItemIndex, newColor),
            VoxelEditor.CurrentPalette.Colors[paletteItemIndex], true, true);
        }

        private void ApplyEditPaletteItem(int paletteItemIndex, VoxelColor newVoxelColor)
        {
            int oldOpacity = (int)VoxelEditor.CurrentPalette.Colors[paletteItemIndex].Color.a;
            bool hasColorChangedOpacityType = oldOpacity != (int)newVoxelColor.Color.a;

            _colorSelector.SetPaletteItemColor(paletteItemIndex, newVoxelColor);
            VoxelEditor.CurrentPalette.SetColor(paletteItemIndex, newVoxelColor);
            VoxelSharedData.RefreshColorBuffer(VoxelEditor.PaletteIndex);
            EditorUtility.SetDirty(VoxelEditor.CurrentPalette);
            _voxelEditor.RefreshPreviewColors(hasColorChangedOpacityType);
        }

        private void OnChangeColor(int colorIndex)
        {
            _colorSelector.SetIndexValue(colorIndex, false);
        }

        private void OnColorSelectorValueChanged(int colorIndex)
        {
            VoxelEditor.ColorIndex = colorIndex;
        }

        protected override void OnStopEditVoxelObject()
        {
        }

        private void OnPressDeletePalette()
        {
            if (_foxEditSettings.Palettes.Count() <= 1)
                return;

            if (EditorUtility.DisplayDialog("Delete Palette", "This will permanently remove the selected palette. This action cannot be undone. Do you want to continue ?", "Delete", "Cancel"))
            {
                int oldIndex = _paletteDropdown.index;
                VoxelPalettesHelper.RemovePaletteAt(_paletteDropdown.index);
                _paletteDropdown.choices = VoxelSharedData.GetPaletteNames().ToList();
                _paletteDropdown.index = Mathf.Clamp(oldIndex, 0, _paletteDropdown.choices.Count() - 1);
                EditorUtility.SetDirty(_foxEditSettings);
            }
        }

        private void OnPressRenamePalette()
        {
            StartRename();
        }

        private void StartRename()
        {
            _paletteSelectionContainer.style.display = DisplayStyle.None;
            _renameField.SetValueWithoutNotify(_paletteDropdown.value);
            _renameField.style.display = DisplayStyle.Flex;

            _renameField.RegisterCallback<KeyDownEvent>(RenameFieldKeyDownEvent);
            _renameField.focusable = true;
            _renameField.schedule.Execute(() =>
            {
                _renameField.Focus();
                _renameField.SelectAll();
            });
        }

        private void RenameFieldKeyDownEvent(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                VoxelPalettesHelper.RenamePaletteAt(_paletteDropdown.index, _renameField.text);
                _paletteDropdown.choices = VoxelSharedData.GetPaletteNames().ToList();
                _paletteDropdown.value = _renameField.value;
                StopRename();
            }
            if (evt.keyCode == KeyCode.Escape)
            {
                StopRename();
            }
        }

        private void StopRename()
        {
            _paletteSelectionContainer.style.display = DisplayStyle.Flex;
            _renameField.style.display = DisplayStyle.None;
            _renameField.UnregisterCallback<KeyDownEvent>(RenameFieldKeyDownEvent);
        }

        private void OnPressAddPalette()
        {
            VoxelPalettesHelper.DuplicatePalette(_paletteDropdown.index);
            _paletteDropdown.choices = VoxelSharedData.GetPaletteNames().ToList();
            _paletteDropdown.index = _paletteDropdown.choices.Count - 1;
        }
    }
}