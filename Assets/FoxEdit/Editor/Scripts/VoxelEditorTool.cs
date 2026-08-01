using System;
using System.Collections.Generic;
using System.Linq;
using FoxEdit.VoxelTools;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace FoxEdit
{
    [EditorTool("Voxel Editor", typeof(VoxelRenderer))]
    internal class VoxelEditorTool : EditorTool
    {
        private VoxelEditor _voxelEditor;
        private GUIContent _icon;
        private bool _isMouseOnVoxel;
        private Vector3 _cubePosition;
        private Vector3 _worldNormal;
        private bool _repaint = false;
        private bool _previousShowGizmos = false;
        private Vector3 editedVoxels;

        #region Initialize
        private void OnEnable()
        {
            _icon = new GUIContent(EditorGUIUtility.Load("d_Prefab On Icon") as Texture2D, "Voxel Editor Tool");
            FoxEditManager.OnStartEditVoxelObject += OnStartEditVoxelObject;
        }

        private void OnStartEditVoxelObject(VoxelObject obj, VoxelRenderer renderer, VoxelEditor voxelEditor)
        {
            Selection.activeGameObject = renderer.gameObject;

            EditorApplication.delayCall += () =>
            {
                if (ToolManager.activeToolType != typeof(VoxelEditorTool))
                    ToolManager.SetActiveTool<VoxelEditorTool>();
            };
        }

        public override void OnActivated()
        {
            if (!TryGetVoxelEditor(out _voxelEditor))
            {
                Debug.Log("Cannot open Voxel Edit Tool on the selected Gameobject");
                Tools.current = Tool.None;
            }
            GizmoUtility.SetGizmoEnabled(typeof(BoxCollider), false);
        }

        void OnDisable()
        {
            GizmoUtility.SetGizmoEnabled(typeof(BoxCollider), true);
        }
        #endregion

        public override void OnToolGUI(EditorWindow window)
        {
            if (_voxelEditor == null)
                return;

            Event e = Event.current;
            if (e.type == EventType.MouseMove)
                OnMouseMove(e);

            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                OnLeftClick(e);
                e.Use();
            }

            if (e.type == EventType.MouseDown && e.button == 2)
            {
                OnMiddleClick(e);
                e.Use();
            }

            if (_isMouseOnVoxel)
                DrawChangePreview();
            if (_repaint)
            {
                window.Repaint();
                _repaint = false;
            }
        }

        private void DrawChangePreview()
        {
            VoxelEditorFrame voxelEditorFrame = _voxelEditor.CurrentFrame;

            if (voxelEditorFrame == null)
                return;

            Handles.color = GetChangePreviewColor();
            List<Vector3Int> editedVoxelsGridPositions = GetChangePreviewList(voxelEditorFrame);

            if (editedVoxels == null)
                return;

            Vector3 worldPosition = Vector3.zero;
            Vector3 offsetedCubePosition = Vector3.zero;

            foreach (Vector3Int editedVoxelGridPosition in editedVoxelsGridPositions)
            {
                worldPosition = voxelEditorFrame.GridToWorldPosition(editedVoxelGridPosition);
                offsetedCubePosition = worldPosition + new Vector3(0f, 0.05f, 0f);
                Handles.DrawWireCube(offsetedCubePosition, Vector3.one * 0.1f);
            }
        }

        private List<Vector3Int> GetChangePreviewList(VoxelEditorFrame voxelEditorFrame)
        {
            Vector3Int gridPosition = voxelEditorFrame.WorldToGridPosition(_cubePosition);
            Vector3Int direction = voxelEditorFrame.NormalToDirection(_worldNormal);
            List<Vector3Int> editedVoxels = new List<Vector3Int>();

            if (VoxelEditor.Action == vxAction.Paint)
            {
                if (VoxelEditor.Tool == vxTool.Fill)
                {
                    if (voxelEditorFrame.TryGetLayerToAdd(out editedVoxels, gridPosition, direction))
                        return editedVoxels;
                }
                else if (VoxelEditor.Tool == vxTool.Brush)
                {
                    if (voxelEditorFrame.CanAddVoxel(out Vector3Int newVoxel, gridPosition, direction))
                        return new List<Vector3Int>() { newVoxel };
                }
            }
            else if (VoxelEditor.Action == vxAction.Erase)
            {
                if (VoxelEditor.Tool == vxTool.Fill)
                {
                    if (voxelEditorFrame.TryGetLayerToRremove(out editedVoxels, gridPosition, direction))
                        return editedVoxels;
                }
            }
            else if (VoxelEditor.Action == vxAction.Color)
            {
                if (VoxelEditor.Tool == vxTool.Fill)
                {
                    if (voxelEditorFrame.TryGetAreaToColor(out editedVoxels, gridPosition, VoxelEditor.ColorIndex))
                        return editedVoxels;
                }
            }

            return new List<Vector3Int>() { gridPosition };
        }

        private Color GetChangePreviewColor()
        {
            switch (VoxelEditor.Action)
            {
                case vxAction.Color:
                    return FoxEditEditorSettings.Instance.ToolPaintColor.Value;
                case vxAction.Erase:
                    return FoxEditEditorSettings.Instance.ToolRemoveColor.Value;
                case vxAction.Paint:
                    return FoxEditEditorSettings.Instance.ToolAddColor.Value;
            }

            return Color.magenta;
        }

        private void OnMouseMove(Event e)
        {
            if (_voxelEditor.CurrentFrame.VoxelRaycast(HandleUtility.GUIPointToWorldRay(e.mousePosition), out VoxelEditorObject voxel, out Vector3 faceNormal))
            {
                if (voxel.WorldPosition != _cubePosition)
                    _repaint = true;
                if (faceNormal != _worldNormal)
                    _repaint = true;
                _cubePosition = voxel.WorldPosition;
                _worldNormal = faceNormal;
                _isMouseOnVoxel = true;
            }
            else
            {
                _isMouseOnVoxel = false;
                _repaint = true;
            }
        }

        private void OnLeftClick(Event e)
        {
            if (!_isMouseOnVoxel) return;
            OnMouseMove(e);
            _voxelEditor.UseTool(_cubePosition, _worldNormal);
            _isMouseOnVoxel = false;
        }

        private void OnMiddleClick(Event e)
        {
            if (_voxelEditor.CurrentFrame.VoxelRaycast(HandleUtility.GUIPointToWorldRay(e.mousePosition), out VoxelEditorObject voxel, out Vector3 faceNormal))
            {
                Vector3Int gridPosition = _voxelEditor.CurrentFrame.WorldToGridPosition(voxel.WorldPosition);
                VoxelEditorObject voxelEditorObject = _voxelEditor.GetVoxelEditorObject(gridPosition);

                VoxelEditor.ColorIndex = voxelEditorObject.ColorIndex;
            }
        }

        private bool TryGetVoxelEditor(out VoxelEditor voxelEditor)
        {
            voxelEditor = null;
            if (FoxEditManager.VoxelEditor == null)
            {
                if (TryGetSelectedVoxelRenderer(out VoxelRenderer voxelRenderer))
                {
                    _voxelEditor = FoxEditManager.StartEditVoxelObject(voxelRenderer);
                    return true;
                }
                else
                {
                    return false;
                }
            }
            voxelEditor = FoxEditManager.VoxelEditor;
            return voxelEditor != null;
        }

        private bool TryGetSelectedVoxelRenderer(out VoxelRenderer voxelRenderer)
        {
            GameObject selectedGameobject = Selection.activeGameObject;
            voxelRenderer = null;

            if (selectedGameobject == null)
                return false;

            voxelRenderer = selectedGameobject.GetComponent<VoxelRenderer>();
            return voxelRenderer != null;
        }

        public override GUIContent toolbarIcon => _icon;
    }
}