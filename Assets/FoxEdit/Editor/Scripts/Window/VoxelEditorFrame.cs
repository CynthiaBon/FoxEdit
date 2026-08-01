using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static FoxEdit.VoxelObject;

namespace FoxEdit
{
    internal class VoxelEditorFrame
    {
        public Transform VoxelTransform { get; private set; }
        private VoxelEditor _editWindow = null;
        private Grid3D _grid = null;
        public Texture2D thumbnail = null;

        public bool VoxelRaycast(Ray ray, out VoxelEditorObject voxel, out Vector3 faceNormal)
        {
            Vector3Int gridSpacePosition = WorldToGridPosition(ray.origin);
            Vector3 roundedPosition = GridToWorldPosition(gridSpacePosition);
            Vector3 difference = roundedPosition - ray.origin;

            if (difference.y > 0.0f)
                gridSpacePosition.y -= 1;
            roundedPosition = GridToWorldPosition(gridSpacePosition);

            Vector3 voxelSpacePosition = ray.origin - roundedPosition;

            if (_grid.VoxelRaycast(gridSpacePosition, voxelSpacePosition, ray.direction, this, out voxel, out faceNormal))
            {
                faceNormal = (VoxelTransform.rotation * faceNormal).normalized;
                return true;
            }
            return false;
        }

        #region Initialization

        internal VoxelEditorFrame(Transform voxelTransform, int frameIndex, VoxelEditor editWindow)
        {
            VoxelTransform = voxelTransform;
            _editWindow = editWindow;

            _grid = new Grid3D();
        }

        internal void LoadFromSave(EditorFrameVoxels editorVoxels, int paletteIndex)
        {
            for (int i = 0; i < editorVoxels.VoxelPositions.Length; i++)
            {
                Vector3Int position = editorVoxels.VoxelPositions[i];
                _grid[position] = CreateVoxelObject(position);
                SetColor(position, paletteIndex, editorVoxels.ColorIndices[i]);
            }
        }

        private void LoadFromCopy(Grid3D gridCopy)
        {
            _grid = gridCopy;
        }

        internal VoxelEditorFrame GetCopy(int newFrameIndex, int paletteIndex)
        {
            VoxelEditorFrame newFrame = new VoxelEditorFrame(VoxelTransform, newFrameIndex, _editWindow);
            Grid3D otherGrid = new Grid3D();

            foreach (Vector3Int gridPosition in _grid.Keys)
            {
                VoxelEditorObject voxelObject = newFrame.CreateVoxelObject(gridPosition);
                int colorIndex = _grid[gridPosition].ColorIndex;
                voxelObject.SetColor(colorIndex);
                otherGrid[gridPosition] = voxelObject;
            }

            newFrame.LoadFromCopy(otherGrid);
            return newFrame;
        }

        #endregion Initialization

        #region Editing

        internal void Show()
        {
            VoxelTransform.gameObject.SetActive(true);
        }

        internal void Hide()
        {
            VoxelTransform.gameObject.SetActive(false);
        }

        internal bool TryAddVoxelNextTo(Vector3Int gridPosition, Vector3Int direction, int paletteIndex, int colorIndex)
        {
            if (CanAddVoxel(out Vector3Int newGridPosition, gridPosition, direction))
            {
                _grid[newGridPosition] = CreateVoxelObject(newGridPosition);
                SetColor(newGridPosition, paletteIndex, colorIndex);

                return true;
            }

            return false;
        }

        internal bool CanAddVoxel(out Vector3Int newVoxel, Vector3Int gridPosition, Vector3Int direction)
        {
            newVoxel = Vector3Int.zero;
            if (_grid.IsEmpty(gridPosition) && gridPosition != Vector3Int.zero)
                return false;

            Vector3Int newGridPosition = gridPosition + direction;

            if (!_grid.IsEmpty(newGridPosition))
                return false;
            newVoxel = newGridPosition;
            return true;
        }

        internal bool TryAddLayer(Vector3Int gridPosition, Vector3Int direction, int paletteIndex, int colorIndex)
        {
            if (_grid.IsEmpty(gridPosition))
                return false;

            int baseColorIndex = _grid[gridPosition].ColorIndex;
            HashSet<Vector3Int> voxelsToAdd = new HashSet<Vector3Int>();

            if (AddLayer(ref voxelsToAdd, gridPosition, direction, baseColorIndex))
            {
                foreach (Vector3Int newGridPosition in voxelsToAdd)
                {
                    Vector3Int newGridPositionOffset = newGridPosition + direction;
                    if (_grid.IsEmpty(newGridPositionOffset))
                    {
                        _grid[newGridPositionOffset] = CreateVoxelObject(newGridPositionOffset);
                        SetColor(newGridPositionOffset, paletteIndex, colorIndex);
                    }
                }
                return true;
            }

            return false;
        }

        internal bool TryGetLayerToAdd(out List<Vector3Int> editedVoxels, Vector3Int gridPosition, Vector3Int direction)
        {
            editedVoxels = null;
            if (_grid.IsEmpty(gridPosition))
                return false;

            int baseColorIndex = _grid[gridPosition].ColorIndex;
            HashSet<Vector3Int> voxelsToAdd = new HashSet<Vector3Int>();

            if (AddLayer(ref voxelsToAdd, gridPosition, direction, baseColorIndex))
            {
                editedVoxels = voxelsToAdd.ToList();
                for (int i = 0; i < editedVoxels.Count; i++)
                {
                    editedVoxels[i] += direction;
                }
                return true;
            }

            return false;
        }

        private bool AddLayer(ref HashSet<Vector3Int> voxelsToAdd, Vector3Int gridPosition, Vector3Int direction, int baseColorIndex)
        {
            Vector3Int newGridPosition = gridPosition + direction;

            if (_grid.IsEmpty(gridPosition) ||
                !_grid.IsEmpty(newGridPosition) ||
                _grid[gridPosition].ColorIndex != baseColorIndex ||
                !voxelsToAdd.Add(gridPosition))
                return false;

            Vector3Int tangent = new Vector3Int(direction.z, direction.x, direction.y);
            AddLayer(ref voxelsToAdd, gridPosition + tangent, direction, baseColorIndex);
            AddLayer(ref voxelsToAdd, gridPosition - tangent, direction, baseColorIndex);

            Vector3Int bitangent = new Vector3Int(direction.y, direction.z, direction.x);
            AddLayer(ref voxelsToAdd, gridPosition + bitangent, direction, baseColorIndex);
            AddLayer(ref voxelsToAdd, gridPosition - bitangent, direction, baseColorIndex);

            return true;
        }

        internal bool TryRemoveVoxel(Vector3Int gridPosition)
        {
            if (_grid.IsEmpty(gridPosition) || _grid.Count == 1)
                return false;

            _grid.Remove(gridPosition);
            return true;
        }

        internal bool TryRemoveLayer(Vector3Int gridPosition, Vector3Int direction)
        {
            if (_grid.IsEmpty(gridPosition))
                return false;

            int colorIndex = _grid[gridPosition].ColorIndex;
            HashSet<Vector3Int> voxelsToRemove = new HashSet<Vector3Int>();

            if (RemoveLayer(ref voxelsToRemove, gridPosition, direction, colorIndex))
            {
                foreach (Vector3Int voxelToRemove in voxelsToRemove)
                {
                    _grid.Remove(voxelToRemove);
                }
                return true;
            }
            return false;
        }

        internal bool TryGetLayerToRremove(out List<Vector3Int> editedVoxels, Vector3Int gridPosition, Vector3Int direction)
        {
            editedVoxels = null;
            if (_grid.IsEmpty(gridPosition))
                return false;

            int colorIndex = _grid[gridPosition].ColorIndex;
            HashSet<Vector3Int> voxelsToRemove = new HashSet<Vector3Int>();
            if (RemoveLayer(ref voxelsToRemove, gridPosition, direction, colorIndex))
            {
                editedVoxels = voxelsToRemove.ToList();
                return true;
            }
            return false;
        }

        private bool RemoveLayer(ref HashSet<Vector3Int> voxelsToRemove, Vector3Int gridPosition, Vector3Int direction, int colorIndex)
        {
            if (_grid.IsEmpty(gridPosition) ||
                voxelsToRemove.Count == _grid.Count - 1 ||
                _grid[gridPosition].ColorIndex != colorIndex ||
                !voxelsToRemove.Add(gridPosition))
                return false;

            Vector3Int tangent = new Vector3Int(direction.z, direction.x, direction.y);
            RemoveLayer(ref voxelsToRemove, gridPosition + tangent, direction, colorIndex);
            RemoveLayer(ref voxelsToRemove, gridPosition - tangent, direction, colorIndex);

            Vector3Int bitangent = new Vector3Int(direction.y, direction.z, direction.x);
            RemoveLayer(ref voxelsToRemove, gridPosition + bitangent, direction, colorIndex);
            RemoveLayer(ref voxelsToRemove, gridPosition - bitangent, direction, colorIndex);

            return voxelsToRemove.Count != 0;
        }

        internal bool TryColorVoxel(Vector3Int gridPosition, int paletteIndex, int colorIndex)
        {
            if (_grid.IsEmpty(gridPosition) || _grid[gridPosition].ColorIndex == colorIndex)
                return false;

            SetColor(gridPosition, paletteIndex, colorIndex);
            return true;
        }

        internal bool TryFillColor(Vector3Int gridPosition, int paletteIndex, int colorIndex)
        {
            if (_grid.IsEmpty(gridPosition))
                return false;

            int baseColorIndex = _grid[gridPosition].ColorIndex;
            HashSet<Vector3Int> voxelsToColor = new HashSet<Vector3Int>();

            if (FillColor(ref voxelsToColor, gridPosition, colorIndex, baseColorIndex))
            {
                foreach (Vector3Int coloredVoxelPosition in voxelsToColor)
                {
                    SetColor(coloredVoxelPosition, paletteIndex, colorIndex);
                }
                return true;
            }

            return false;
        }

        internal bool TryGetAreaToColor(out List<Vector3Int> editedVoxels, Vector3Int gridPosition, int colorIndex)
        {
            editedVoxels = null;
            if (_grid.IsEmpty(gridPosition))
                return false;

            int baseColorIndex = _grid[gridPosition].ColorIndex;
            HashSet<Vector3Int> voxelsToColor = new HashSet<Vector3Int>();

            if (FillColor(ref voxelsToColor, gridPosition, colorIndex, baseColorIndex))
            {
                editedVoxels = voxelsToColor.ToList();
                return true;
            }

            return false;
        }

        private bool FillColor(ref HashSet<Vector3Int> voxelsToColor, Vector3Int gridPosition, int colorIndex, int baseColorIndex)
        {
            if (_grid.IsEmpty(gridPosition) ||
                _grid[gridPosition].ColorIndex == colorIndex ||
                _grid[gridPosition].ColorIndex != baseColorIndex ||
                !voxelsToColor.Add(gridPosition))
                return false;

            FillColor(ref voxelsToColor, gridPosition + Vector3Int.up, colorIndex, baseColorIndex);
            FillColor(ref voxelsToColor, gridPosition + Vector3Int.down, colorIndex, baseColorIndex);
            FillColor(ref voxelsToColor, gridPosition + Vector3Int.left, colorIndex, baseColorIndex);
            FillColor(ref voxelsToColor, gridPosition + Vector3Int.right, colorIndex, baseColorIndex);
            FillColor(ref voxelsToColor, gridPosition + Vector3Int.forward, colorIndex, baseColorIndex);
            FillColor(ref voxelsToColor, gridPosition + Vector3Int.back, colorIndex, baseColorIndex);

            return true;
        }

        internal void ApplyVoxelTransform(int paletteIndex)
        {
            //List<GameObject> selectedVoxels = Selection.gameObjects.ToList();
            //float voxelScale = selectedVoxels[0].transform.localScale.x;
            //if (voxelScale < 1.0f)
            //    ScaleDown(selectedVoxels, voxelScale, paletteIndex);
            //else if (voxelScale > 1.0f)
            //    ScaleUp(selectedVoxels, voxelScale, paletteIndex);
            //else
            //    SnapToGrid(selectedVoxels);
        }

        //private void SnapToGrid(List<GameObject> selectedVoxels)
        //{
        //    Vector3Int[] gridPositions = _grid.Keys.ToArray();
        //    Grid3D gridCopy = new Grid3D();
        //    RotationSnap(selectedVoxels);
        //    List<Vector3Int> selectedGridPosition = new List<Vector3Int>();

        //    for (int i = 0; i < gridPositions.Length; i++)
        //    {
        //        Vector3Int gridPosition = gridPositions[i];
        //        if (!selectedVoxels.Contains(_grid[gridPosition].GameObject))
        //            gridCopy[gridPosition] = _grid[gridPosition];
        //        else
        //            selectedGridPosition.Add(gridPosition);
        //    }

        //    for (int i = 0; i < selectedGridPosition.Count; i++)
        //    {
        //        Vector3Int gridPosition = selectedGridPosition[i];
        //        Vector3 worldPosition = _grid[gridPosition].WorldPosition;
        //        Vector3Int newGridPosition = WorldToGridPosition(worldPosition);

        //        if (gridCopy.IsEmpty(newGridPosition))
        //        {
        //            VoxelEditorObject voxel = _grid[gridPosition];
        //            Vector3 localPosition = GridToLocalPosition(newGridPosition);
        //            voxel.SetPosition(localPosition, newGridPosition);
        //            gridCopy[newGridPosition] = voxel;
        //        }
        //    }

        //    _grid = gridCopy;
        //}

        //private void ScaleUp(List<GameObject> selectedVoxels, float scale, int paletteIndex)
        //{
        //    int roundedScale = Mathf.RoundToInt(scale);
        //    Vector3Int[] gridPositions = _grid.Keys.ToArray();
        //    Grid3D gridCopy = new Grid3D();

        //    for (int i = 0; i < gridPositions.Length; i++)
        //    {
        //        Vector3Int gridPosition = gridPositions[i];
        //        Vector3 localPosition = GridToLocalPosition(gridPosition);
        //        _grid[gridPosition].SetPosition(localPosition, gridPosition);
        //    }

        //    if (roundedScale == 1)
        //        return;

        //    for (int i = 0; i < gridPositions.Length; i++)
        //    {
        //        Vector3Int gridPosition = gridPositions[i];
        //        if (!selectedVoxels.Contains(_grid[gridPosition].GameObject))
        //        {
        //            gridCopy[gridPosition] = _grid[gridPosition];
        //            continue;
        //        }

        //        for (int x = 0; x < roundedScale; x++)
        //        {
        //            for (int y = 0; y < roundedScale; y++)
        //            {
        //                for (int z = 0; z < roundedScale; z++)
        //                {
        //                    int colorIndex = _grid[gridPosition].ColorIndex;
        //                    Vector3Int initialGridPosition = gridPosition * roundedScale;
        //                    Vector3Int offset = new Vector3Int(x, y, z);
        //                    if (x == 0 && y == 0 && z == 0)
        //                    {
        //                        gridCopy[initialGridPosition] = _grid[gridPosition];
        //                        Vector3 localPosition = GridToLocalPosition(initialGridPosition);
        //                        gridCopy[initialGridPosition].SetPosition(localPosition, initialGridPosition);
        //                    }
        //                    else
        //                    {
        //                        Vector3Int newGridPosition = initialGridPosition + offset;
        //                        gridCopy[newGridPosition] = CreateVoxelObject(newGridPosition);
        //                        Material material = _editWindow.GetMaterial(paletteIndex, colorIndex);
        //                        gridCopy[newGridPosition].SetColor(material, colorIndex);
        //                    }
        //                }
        //            }
        //        }
        //    }

        //    _grid = gridCopy;
        //}

        //private void ScaleDown(List<GameObject> selectedVoxels, float scale, int paletteIndex)
        //{
        //    int roundedScale = Mathf.RoundToInt(1.0f / scale);
        //    if (roundedScale == 1)
        //        return;

        //    Grid3D gridCopy = new Grid3D();

        //    for (int x = _grid.Min.x; x < _grid.Max.x; x += roundedScale)
        //    {
        //        for (int y = _grid.Min.y; y < _grid.Max.y; y += roundedScale)
        //        {
        //            for (int z = _grid.Min.z; z < _grid.Max.z; z += roundedScale)
        //            {
        //                gridCopy = MergeVoxels(selectedVoxels, gridCopy, new Vector3Int(x, y, z), roundedScale, paletteIndex);
        //            }
        //        }
        //    }

        //    _grid = gridCopy;
        //}

        //private Grid3D MergeVoxels(List<GameObject> selectedVoxels, Grid3D gridCopy, Vector3Int basePosition, int roundedScale, int paletteIndex)
        //{
        //    List<int> colorIndices = new List<int>();
        //    VoxelEditorObject baseVoxel = null;

        //    for (int x = 0; x < roundedScale; x++)
        //    {
        //        for (int y = 0; y < roundedScale; y++)
        //        {
        //            for (int z = 0; z < roundedScale; z++)
        //            {
        //                Vector3Int offsetPosition = basePosition + new Vector3Int(x, y, z);

        //                VoxelEditorObject voxel = _grid[offsetPosition];
        //                if (voxel == null)
        //                    continue;

        //                if (!selectedVoxels.Contains(voxel.GameObject))
        //                {
        //                    gridCopy[voxel.GridPosition] = voxel;
        //                    continue;
        //                }

        //                colorIndices.Add(voxel.ColorIndex);

        //                if (baseVoxel == null)
        //                {
        //                    baseVoxel = voxel;
        //                    Vector3Int newPosition = DividePosition(basePosition, roundedScale);
        //                    Vector3 localPosition = GridToLocalPosition(newPosition);
        //                    voxel.SetPosition(localPosition, newPosition);
        //                    gridCopy[newPosition] = voxel;
        //                }
        //            }
        //        }
        //    }

        //    if (baseVoxel != null)
        //    {
        //        int colorIndex = colorIndices.GroupBy(index => index).OrderByDescending(item => item.Count()).First().Key;
        //        Material material = _editWindow.GetMaterial(paletteIndex, colorIndex);
        //        baseVoxel.SetColor(material, colorIndex);
        //    }

        //    return gridCopy;
        //}

        public VoxelEditorObject GetVoxelEditorObject(Vector3Int cubePosition)
        {
            return _grid[cubePosition];
        }

        private Vector3Int DividePosition(Vector3Int position, int divide)
        {
            return new Vector3Int(Mathf.FloorToInt(position.x / (float)divide), Mathf.FloorToInt(position.y / (float)divide), Mathf.FloorToInt(position.z / (float)divide));
        }

        private void RotationSnap(List<GameObject> selection)
        {
            Vector3 eulerAngles = selection[0].transform.eulerAngles;
            float angle = eulerAngles.magnitude;
            if (angle == 0.0f)
                return;

            Vector3 axis = eulerAngles.normalized;
            Vector3 center = GetCenter(selection);
            float snapAngle = Mathf.Round(angle / 45.0f);
            snapAngle *= 45.0f;

            for (int i = 0; i < selection.Count; i++)
            {
                selection[i].transform.RotateAround(center, axis, -angle);
                selection[i].transform.RotateAround(center, axis, snapAngle);
            }
        }

        private Vector3 GetCenter(List<GameObject> selection)
        {
            Vector3 center = Vector3.zero;
            for (int i = 0; i < selection.Count; i++)
            {
                center += selection[i].transform.position;
            }
            return center / selection.Count;
        }

        private VoxelEditorObject CreateVoxelObject(Vector3Int gridPosition)
        {
            Vector3 worldPosition = GridToWorldPosition(gridPosition);
            VoxelEditorObject voxelObject = new VoxelEditorObject(worldPosition, gridPosition);

            return voxelObject;
        }

        private void SetColor(Vector3Int gridPosition, int paletteIndex, int colorIndex)
        {
            if (_grid.IsEmpty(gridPosition))
                return;

            _grid[gridPosition].SetColor(colorIndex);
        }

        public void UpdatePalette(int paletteIndex)
        {
            int colorIndex = -1;

            foreach (VoxelEditorObject obj in _grid)
            {
                if (obj == null)
                    continue;
                colorIndex = obj.ColorIndex;
                obj.SetColor(colorIndex);
            }
        }

        internal void Destroy()
        {
            _grid.Clear();
        }

        #endregion Editing

        #region SpaceConversion

        public static Vector3 GridToLocalPosition(Vector3Int position)
        {
            return new Vector3(position.x, position.y, position.z) * 0.1f;
        }

        public Vector3Int WorldToGridPosition(Vector3 worldPosition)
        {
            Vector3 localPosition = VoxelTransform.InverseTransformPoint(worldPosition);
            localPosition *= 10.0f;
            return new Vector3Int(Mathf.RoundToInt(localPosition.x), Mathf.RoundToInt(localPosition.y), Mathf.RoundToInt(localPosition.z));
        }

        public Vector3 GridToWorldPosition(Vector3Int gridPosition)
        {
            Vector3 localPosition = (Vector3)gridPosition;
            localPosition *= 0.1f;
            return VoxelTransform.TransformPoint(localPosition);
        }

        public Vector3Int NormalToDirection(Vector3 normal)
        {
            Quaternion inverseRotation = Quaternion.Inverse(VoxelTransform.rotation);
            normal = inverseRotation * normal;

            return new Vector3Int(Mathf.RoundToInt(normal.x), Mathf.RoundToInt(normal.y), Mathf.RoundToInt(normal.z));
        }

        #endregion SpaceConversion

        #region SaveSystem

        internal VoxelObjectPackedFrameData GetPackedData()
        {
            Vector3Int minBounds;
            Vector3Int maxBounds;
            GetBounds(out minBounds, out maxBounds);

            VoxelObjectPackedFrameData packedData = new VoxelObjectPackedFrameData
            {
                MinBounds = minBounds,
                MaxBounds = maxBounds,
                VoxelPositionToColor = _grid.GetPositionToColor()
            };

            return packedData;
        }

        //TODO: compute in realtime
        private void GetBounds(out Vector3Int min, out Vector3Int max)
        {
            min = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
            max = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);

            foreach (Vector3Int position in _grid.Keys)
            {
                min.x = Mathf.Min(position.x, min.x);
                min.y = Mathf.Min(position.y, min.y);
                min.z = Mathf.Min(position.z, min.z);

                max.x = Mathf.Max(position.x, max.x);
                max.y = Mathf.Max(position.y, max.y);
                max.z = Mathf.Max(position.z, max.z);
            }
        }

        #endregion SaveSystem

#if UNITY_EDITOR
        #region Debug

        public void ShowGrid(float duration = 1.0f, int customSize = -1)
        {
            ShowGrid(Color.darkGreen, duration, customSize);
        }

        public void ShowGrid(Color color, float duration = 1.0f, int customSize = -1)
        {
            Vector3Int size = _grid.Max - _grid.Min + Vector3Int.one;
            Vector3Int min = _grid.Min;

            if (customSize >= 0)
            {
                size = new Vector3Int(customSize, customSize, customSize);
                min = new Vector3Int(customSize, customSize, customSize) / -2;
            }

            DebugDrawHelper.DebugDrawGrid(GridToWorldPosition(min) + new Vector3(-0.05f, 0.0f, -0.05f), size, 0.1f, color, duration);
        }

        #endregion Debug
#endif
    }
}
