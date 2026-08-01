
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FoxEdit.VoxelTools;
using System.Linq;
using System;
using System.Threading.Tasks;
using System.IO;
using System.Collections;

namespace FoxEdit
{
    internal partial class VoxelEditor : IDisposable
    {
        #region STATIC_FIELDS
        public static event Action<vxTool> OnChangeTool;
        public static event Action<vxAction> OnChangeAction;
        public static event Action<int> OnChangeColor;
        public static event Action<int> OnChangePalette;

        private static vxAction _action = vxAction.Paint;
        public static vxAction Action
        {
            get => _action;
            set
            {
                if (_action == value)
                    return;
                _action = value;
                OnChangeAction?.Invoke(_action);
            }
        }

        private static vxTool _tool = vxTool.Brush;
        public static vxTool Tool
        {
            get => _tool;
            set
            {
                if (_tool == value)
                    return;
                _tool = value;
                OnChangeTool?.Invoke(_tool);
            }
        }

        private static int _colorIndex;
        public static int ColorIndex
        {
            get => _colorIndex;
            set
            {
                _colorIndex = Mathf.Clamp(value, 0, CurrentPalette.Colors.Length - 1);
                OnChangeColor?.Invoke(_colorIndex);
            }
        }

        private static int _paletteIndex;
        public static int PaletteIndex
        {
            get => _paletteIndex;
            set
            {
                if (_paletteIndex == value)
                    return;
                _paletteIndex = Mathf.Clamp(value, 0, VoxelSharedData.GetPaletteCount() - 1);
                ColorIndex = ColorIndex;
                OnChangePalette?.Invoke(_paletteIndex);
            }
        }

        public static VoxelPalette CurrentPalette => VoxelSharedData.GetPalette(PaletteIndex);

        #endregion
        //Thumbnails
        public event Action<int, int, Texture2D> OnFramesThumbnailsUpdated;
        //Flags
        private bool _edit = false;
        public bool IsDirty { get; private set; } = false;

        //Mesh
        private VoxelRenderer _voxelRenderer = null;

        //Preview
        private VoxelPreview _preview = null;
        private double _lastEditorTime = 0.0d;
        private double _animationPreviewTimer = 0.0d;
        private int _previewAnimationIndex = 0;
        private bool _isAnimationPreviewPaused = false;
        private bool _canStopAnimationPreview = false;

        public event Action<int> OnFrameIndexChanged;
        private VoxelEditorFrame lastVisibleEditorFrame = null;
        private int _selectedFrameIndex = 0;
        public int SelectedFrameIndex
        {
            get => _selectedFrameIndex;
            set
            {
                _selectedFrameIndex = value;
                OnFrameIndexChanged?.Invoke(_selectedFrameIndex);
                _preview.ChangeFrame(CurrentFrame);
            }
        }
        public VoxelEditorFrame CurrentFrame
        {
            get
            {
                if (CurrentAnimation == null)
                    return null;
                return CurrentAnimation[SelectedFrameIndex];
            }
        }

        public event Action<int> OnAnimationIndexChanged;
        private int _selectedAnimationIndex = 0;
        public int SelectedAnimationIndex
        {
            get => _selectedAnimationIndex;
            set
            {
                _selectedAnimationIndex = value;
                ChangeFrame(0);
                OnAnimationIndexChanged?.Invoke(_selectedAnimationIndex);
            }
        }

        private int _lastFrameCount = -1;

        public VoxelEditorAnimation CurrentAnimation
        {
            get
            {
                if (_animationList == null || _animationList.Count == 0 || SelectedAnimationIndex < 0 || SelectedAnimationIndex >= _animationList.Count)
                    return null;
                return _animationList[_selectedAnimationIndex];
            }
        }

        public bool IsAnimationPreviewPlay => _canStopAnimationPreview && !_isAnimationPreviewPaused;
        public bool CanStopAnimationPreview => _canStopAnimationPreview;

        //Scene editor voxels
        private List<VoxelEditorAnimation> _animationList;

        #region Init

        public VoxelEditor(VoxelRenderer voxelRenderer)
        {
            _voxelRenderer = voxelRenderer;
            _animationList = new List<VoxelEditorAnimation>();
            _preview = new VoxelPreview(CurrentFrame, _paletteIndex, Color.black);
            VoxelEditor.OnChangePalette += OnPaletteChanged;
            VoxelEditor.OnChangePalette += _preview.SetPaletteIndex;
            _voxelRenderer?.HideMesh();

            if (!_edit)
                EnableEditing();

            SceneView.duringSceneGui += DrawPreview;
        }

        private void DrawPreview(SceneView sceneView)
        {
            if (_lastFrameCount == Time.frameCount)
                return;

            _lastFrameCount = Time.frameCount;
            _preview?.DrawPreview();
        }

        #endregion

        private void EnableEditing()
        {
            if (_voxelRenderer == null)
                return;

            if (_animationList == null || SelectedFrameIndex >= _animationList.Count)
                SelectedFrameIndex = 0;

            VoxelObject voxelObject = _voxelRenderer.VoxelObject;
            if (voxelObject != null)
                PaletteIndex = voxelObject.PaletteIndex;
            else
                PaletteIndex = 0;

            if (_animationList.Count < 0)
                _animationList.Clear();

            string objectName = null;
            if (voxelObject == null)
                objectName = "New voxel object";
            else
                objectName = voxelObject.name;

            _animationList = new List<VoxelEditorAnimation>();

            if (voxelObject != null)
            {
                for (int animation = 0; animation < voxelObject.Animations.Length; animation++)
                {
                    _animationList.Add(new VoxelEditorAnimation(voxelObject.Animations[animation].AnimName, voxelObject.Animations[animation].FrameDuration));
                    for (int i = 0; i < voxelObject.Animations[animation].FrameCount; i++)
                    {
                        VoxelEditorFrame frame = new VoxelEditorFrame(_voxelRenderer.transform, i, this);
                        frame.LoadFromSave(voxelObject.Animations[animation].EditorVoxels[i], PaletteIndex);
                        if (i != _selectedFrameIndex || animation != 0)
                            frame.Hide();
                        _animationList[animation].AddFrame(frame);
                    }
                }
            }
            else
            {
                NewAnimation("Default");
            }

            _voxelRenderer.enabled = false;
            _edit = true;
        }

        public void UseTool(Vector3 worldPosition, Vector3 worldNormal)
        {
            Vector3Int gridPosition = CurrentFrame.WorldToGridPosition(worldPosition);
            Vector3Int direction = CurrentFrame.NormalToDirection(worldNormal);

            if (Action == vxAction.Paint)
            {
                switch (Tool)
                {
                    case vxTool.Brush:
                        IsDirty = CurrentFrame.TryAddVoxelNextTo(gridPosition, direction, PaletteIndex, ColorIndex);
                        break;
                    case vxTool.Fill:
                        IsDirty = CurrentFrame.TryAddLayer(gridPosition, direction, PaletteIndex, ColorIndex);
                        break;
                }
            }
            else if (Action == vxAction.Erase)
            {
                switch (Tool)
                {
                    case vxTool.Brush:
                        IsDirty = CurrentFrame.TryRemoveVoxel(gridPosition);
                        break;
                    case vxTool.Fill:
                        IsDirty = CurrentFrame.TryRemoveLayer(gridPosition, direction);
                        break;
                }
            }
            else if (Action == vxAction.Color)
            {
                switch (Tool)
                {
                    case vxTool.Brush:
                        IsDirty = CurrentFrame.TryColorVoxel(gridPosition, PaletteIndex, ColorIndex);
                        break;
                    case vxTool.Fill:
                        IsDirty = CurrentFrame.TryFillColor(gridPosition, PaletteIndex, ColorIndex);
                        break;
                }
            }

            if (IsDirty)
                _preview.Refresh();

            IsDirty = true;
        }

        #region Animations
        public VoxelEditorAnimation GetVoxelEditorAnimation(int index)
        {
            return _animationList[index];
        }

        public void DeleteAnimation(int index)
        {
            if (_animationList.Count <= 1)
                return;
            lastVisibleEditorFrame = null;

            _animationList.RemoveAt(index);
            if (index <= SelectedAnimationIndex && SelectedAnimationIndex > 0)
                SelectedAnimationIndex--;
            else
                SelectedAnimationIndex = SelectedAnimationIndex;


            IsDirty = true;
        }

        public void NewAnimation(string animationName)
        {
            _animationList.Add(new VoxelEditorAnimation(animationName));
            if (CurrentAnimation != null && CurrentAnimation.FramesCount != 0)
            {
                VoxelEditorFrame newFrame = CurrentAnimation[SelectedFrameIndex].GetCopy(_animationList.Count, PaletteIndex);
                SelectedAnimationIndex = _animationList.Count - 1;
                CurrentAnimation.AddFrame(newFrame);
            }
            else
            {
                SelectedAnimationIndex = _animationList.Count - 1;
                NewFrame();
            }
            ChangeFrame(CurrentAnimation.FramesCount - 1);

            IsDirty = true;
        }

        public List<string> GetAnimationNames()
        {
            return _animationList.Select(a => a.Name).ToList();
        }

        public void PlayAnimation()
        {
            EditorApplication.update += AnimationPreview;
            _canStopAnimationPreview = true;
            _animationPreviewTimer = 0.0f;
            _lastEditorTime = EditorApplication.timeSinceStartup;
            _previewAnimationIndex = 0;
            _isAnimationPreviewPaused = false;
        }

        public void PauseAnimation()
        {
            _isAnimationPreviewPaused = !_isAnimationPreviewPaused;
            _lastEditorTime = EditorApplication.timeSinceStartup;

            if (_isAnimationPreviewPaused)
                EditorApplication.update -= AnimationPreview;
            else
                EditorApplication.update += AnimationPreview;
        }

        public void StopAnimation()
        {
            EditorApplication.update -= AnimationPreview;
            _canStopAnimationPreview = false;
            _preview.ChangeFrame(CurrentFrame);
        }

        private void AnimationPreview()
        {
            double deltaTime = EditorApplication.timeSinceStartup - _lastEditorTime;
            _lastEditorTime = EditorApplication.timeSinceStartup;
            _animationPreviewTimer += deltaTime;

            if (_animationPreviewTimer < CurrentAnimation.FrameDuration)
                return;
            _animationPreviewTimer -= CurrentAnimation.FrameDuration;
            _preview.ChangeFrame(CurrentAnimation.frames[_previewAnimationIndex]);
            _previewAnimationIndex = (_previewAnimationIndex + 1) % CurrentAnimation.frames.Count;
            SceneView.RepaintAll();
        }

        public void RefreshPreviewColors(bool refreshGreedyMeshing)
        {
            _preview.RefreshColors(refreshGreedyMeshing);
        }
        #endregion
        #region Helpers

        public VoxelEditorObject GetVoxelEditorObject(Vector3Int gridPosition)
        {
            if (CurrentFrame != null)
                return CurrentFrame.GetVoxelEditorObject(gridPosition);
            return null;
        }
        #endregion

        #region Frames
        public void NewFrame()
        {
            VoxelEditorFrame newFrame = new VoxelEditorFrame(_voxelRenderer.transform, _animationList.Count, this);
            newFrame.TryAddVoxelNextTo(Vector3Int.zero, Vector3Int.zero, PaletteIndex, 0);
            CurrentAnimation.AddFrame(newFrame);
            ChangeFrame(CurrentAnimation.FramesCount - 1);

            IsDirty = true;
        }


        private void OnPaletteChanged(int paletteColor)
        {
            UpdateColors();
        }

        public void DeleteFrame()
        {
            CurrentAnimation[SelectedFrameIndex].Destroy();
            CurrentAnimation.RemoveFrameAt(SelectedFrameIndex);

            if (SelectedFrameIndex > 0)
                SelectedFrameIndex -= 1;
            else
                SelectedFrameIndex = 0;
            CurrentAnimation[SelectedFrameIndex].Show();

            IsDirty = true;
        }

        public void DuplicateFrame()
        {
            VoxelEditorFrame newFrame = CurrentAnimation[SelectedFrameIndex].GetCopy(_animationList.Count, PaletteIndex);
            CurrentAnimation.AddFrame(newFrame);

            ChangeFrame(_animationList.Count - 1);

            IsDirty = true;
        }

        public void ChangeFrame(int index)
        {
            if (CurrentAnimation.FramesCount == 0)
                return;
            index = Mathf.Clamp(index, 0, CurrentAnimation.FramesCount - 1);
            if (lastVisibleEditorFrame != null)
                lastVisibleEditorFrame.Hide();
            SelectedFrameIndex = index;
            lastVisibleEditorFrame = CurrentAnimation[SelectedFrameIndex];
            lastVisibleEditorFrame.Show();
            UpdateColors();
        }

        public void MoveFrame(int oldIndex, int newIndex)
        {
            VoxelEditorFrame movedFrame = CurrentAnimation.Move(oldIndex, newIndex);
            for (int i = 0; i < _animationList.Count; i++)
                CurrentAnimation[i].VoxelTransform.name = string.Format("Frame {0}", i);
            movedFrame.VoxelTransform.SetSiblingIndex(newIndex);
            if (oldIndex == SelectedFrameIndex)
                SelectedFrameIndex = _animationList.IndexOf(CurrentAnimation);
        }

        #endregion

        #region Frames Thumbnails
        #endregion
        private void UpdateColors()
        {
            if (CurrentAnimation == null || SelectedFrameIndex < 0 || SelectedFrameIndex >= CurrentAnimation.FramesCount)
                return;
            CurrentAnimation[SelectedFrameIndex].UpdatePalette(PaletteIndex);
        }

        private void DisableEditing(bool isFromReload)
        {
            _edit = false;
            DestroyEditorFrame(isFromReload);
            _voxelRenderer.ShowMesh();
        }

        private void DestroyEditorFrame(bool isFromReload)
        {
            if (_animationList.Count > 0)
                _animationList.Clear();

            if (_voxelRenderer != null && !isFromReload)
                _voxelRenderer.enabled = true;
        }

        public void Dispose()
        {
            DisableEditing(false);
            VoxelEditor.OnChangePalette -= OnPaletteChanged;
            VoxelEditor.OnChangePalette -= _preview.SetPaletteIndex;
            _preview?.Destroy();
            SceneView.duringSceneGui -= DrawPreview;
            EditorApplication.update -= AnimationPreview;
        }

        public void Save(string savePath)
        {
            string directory = Path.GetDirectoryName(savePath);
            string meshName = Path.GetFileNameWithoutExtension(savePath);
            VoxelSaveSystem.Save(meshName, directory, _voxelRenderer, CurrentPalette, PaletteIndex, _animationList);
            IsDirty = false;
        }
    }
}