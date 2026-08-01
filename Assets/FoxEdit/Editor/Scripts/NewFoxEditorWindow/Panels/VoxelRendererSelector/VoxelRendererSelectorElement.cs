using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FoxEdit.EditorUtils;

namespace FoxEdit.WindowPanels
{
    public class VoxelRendererSelectorElement : VisualElement
    {
        #region CLASS_NAMES
        public const string VOXEL_RENDERER_SELECTOR_CLASS_NAME = "voxel-renderer-selector";
        public const string VOXEL_RENDERER_SELECTOR_ITEM_CLASS_NAME = "voxel-renderer-selector-item";
        public const string VOXEL_RENDERER_SELECTOR_ITEM_ICON_CLASS_NAME = "voxel-renderer-selector-item-icon";
        #endregion

        public new class UxmlFactory : UxmlFactory<VoxelRendererSelectorElement, UxmlTraits> { }
        public new class UxmlTraits : VisualElement.UxmlTraits
        {

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);

                VoxelRendererSelectorElement frameSelectorElement = ve as VoxelRendererSelectorElement;
            }
        }

        private List<VoxelObject> voxelRenderers;
        private List<Button> voxelRendererButtons = new List<Button>();
        public event Action<VoxelObject> OnSelectVoxelObject;

        public VoxelRendererSelectorElement()
        {
            AddToClassList(VOXEL_RENDERER_SELECTOR_CLASS_NAME);

            for (int i = 0; i < 5; i++)
            {
                Add(CreateVoxelRendererButtonTemplate());
            }
            Add(CreateAddVoxelButton());
        }

        public void RegisterContextualMenu(VisualElement target)
        {
            target.RegisterCallback<ContextClickEvent>((evt) =>
            {
                GenericDropdownMenu menu = new GenericDropdownMenu();

                menu.AddItem("New empty voxel object", false, NewVoxelObject);

                //Kebab c'est deprecated ici
                menu.DropDown(new Rect(evt.mousePosition, Vector2.zero), target, false);
            });
        }

        private void NewVoxelObject()
        {
            FoxEditManager.StartEditNewVoxelObject();
        }

        public void UpdateVoxelObjectList(List<VoxelObject> voxelObjects)
        {
            this.voxelRenderers = voxelObjects;
            Clear();
            voxelRendererButtons.Clear();

            foreach (VoxelObject voxelObject in voxelObjects)
            {
                Button voxelRendererButton = CreateVoxelRendererButton(voxelObject);

                Add(voxelRendererButton);
            }
            Add(CreateAddVoxelButton());
        }

        private Button CreateVoxelRendererButton(VoxelObject voxelObject)
        {
            Button button = CreateVoxelRendererButtonTemplate();
            button.name = string.Format("{0}-button", voxelObject.name);
            button.text = voxelObject.name;

            button.clicked += () => SelectVoxelRenderer(voxelObject);

            SetIcon(voxelObject, button.Q("icon"));

            return button;
        }

        private Button CreateAddVoxelButton()
        {
            Button button = CreateVoxelRendererButtonTemplate();

            button.name = "add-voxel-object-button";
            button.text = "New voxel object";
            button.clicked += NewVoxelObject;
            return button;
        }

        private Button CreateVoxelRendererButtonTemplate()
        {
            Button button = new Button();
            button.name = "template-button";
            button.AddToClassList(VOXEL_RENDERER_SELECTOR_ITEM_CLASS_NAME);
            button.text = "Button";

            VisualElement icon = new VisualElement();
            icon.name = "icon";
            icon.AddToClassList(VOXEL_RENDERER_SELECTOR_ITEM_ICON_CLASS_NAME);

            button.Add(icon);
            return button;
        }

        private void SelectVoxelRenderer(VoxelObject voxelObject)
        {
            OnSelectVoxelObject?.Invoke(voxelObject);
        }

        private void SetIcon(VoxelObject voxelRenderer, VisualElement visualElement)
        {
            Texture2D texture2D = voxelRenderer.GetPreviewIcon();

            if (texture2D != null)
                visualElement.style.backgroundImage = texture2D;
        }
    }
}