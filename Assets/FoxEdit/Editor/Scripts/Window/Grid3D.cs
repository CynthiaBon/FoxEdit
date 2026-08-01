using FoxEdit;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FoxEdit
{
    internal class Grid3D : IEnumerable
    {
        private Dictionary<Vector3Int, VoxelEditorObject> _grid = null;
        private Vector3Int _min = Vector3Int.zero;
        private Vector3Int _max = Vector3Int.zero;

        private struct PlaneData
        {
            public Vector3 Normal;
            public float Point;
        }
        private PlaneData[] _facePlanes = new PlaneData[6]
        {
            new PlaneData { Normal = new Vector3(-1, 0, 0), Point = 0.05f },
            new PlaneData { Normal = new Vector3(1, 0, 0), Point = -0.05f },
            new PlaneData { Normal = new Vector3(0, -1, 0), Point = 0.1f },
            new PlaneData { Normal = new Vector3(0, 1, 0), Point = 0.0f },
            new PlaneData { Normal = new Vector3(0, 0, -1), Point = 0.05f },
            new PlaneData { Normal = new Vector3(0, 0, 1), Point = -0.05f }
        };

        internal Grid3D()
        {
            _grid = new Dictionary<Vector3Int, VoxelEditorObject>();
        }

        internal int Count { get { return _grid.Count; } }
        internal IEnumerable<Vector3Int> Keys { get { return _grid.Keys; } }
        internal IEnumerable<VoxelEditorObject> Values { get { return _grid.Values; } }
        internal Vector3Int Min { get { return _min; } }
        internal Vector3Int Max { get { return _max; } }

        public VoxelEditorObject this[Vector3Int position]
        {
            get { return _grid.TryGetValue(position, out VoxelEditorObject voxel) ? voxel : null; }
            set { _grid[position] = value; TryExpendBounds(position); }
        }

        public IEnumerator GetEnumerator()
        {
            return new GridEnumerator(_grid, _min, _max);
        }

        internal bool IsEmpty(Vector3Int position)
        {
            return !_grid.ContainsKey(position);
        }

        internal void Remove(Vector3Int position)
        {
            if (_grid.ContainsKey(position))
            {
                _grid.Remove(position);
                TryShrinkBounds(position);
            }
        }

        private void TryExpendBounds(Vector3Int newPosition)
        {
            _min.x = Mathf.Min(_min.x, newPosition.x);
            _min.y = Mathf.Min(_min.y, newPosition.y);
            _min.z = Mathf.Min(_min.z, newPosition.z);

            _max.x = Mathf.Max(_max.x, newPosition.x);
            _max.y = Mathf.Max(_max.y, newPosition.y);
            _max.z = Mathf.Max(_max.z, newPosition.z);
        }

        private void TryShrinkBounds(Vector3Int deletedPosition)
        {
            IEnumerable<Vector3Int> keys = _grid.Keys;

            if (deletedPosition.x == _min.x)
                _min.x = keys.Select(p => p.x).Min();
            else if (deletedPosition.x == _max.x)
                _max.x = keys.Select(p => p.x).Max();

            if (deletedPosition.y == _min.y)
                _min.y = keys.Select(p => p.y).Min();
            else if (deletedPosition.y == _max.y)
                _max.y = keys.Select(p => p.y).Max();

            if (deletedPosition.z == _min.z)
                _min.z = keys.Select(p => p.z).Min();
            else if (deletedPosition.z == _max.z)
                _max.z = keys.Select(p => p.z).Max();
        }

        internal void Clear()
        {
            _grid.Clear();
        }

        internal bool VoxelRaycast(Vector3Int gridSpacePosition, Vector3 voxelSpacePosition, Vector3 direction, VoxelEditorFrame parent, out VoxelEditorObject voxel, out Vector3 faceNormal)
        {
            bool[] boundDirections = new bool[3]
            {
                direction.x >= 0.0f,
                direction.y >= 0.0f,
                direction.z >= 0.0f
            };

            PlaneData[] planes = new PlaneData[3]
            {
                boundDirections[0] ? _facePlanes[0] : _facePlanes[1],
                boundDirections[1] ? _facePlanes[2] : _facePlanes[3],
                boundDirections[2] ? _facePlanes[4] : _facePlanes[5]
            };

            faceNormal = Vector3.zero;
            voxel = GetIntersectedVoxel(gridSpacePosition, voxelSpacePosition, direction, planes, boundDirections, parent, ref faceNormal);

            if (voxel != null)
                return true;
            return false;
        }

        private VoxelEditorObject GetIntersectedVoxel(Vector3Int gridSpacePosition, Vector3 voxelSpacePosition, Vector3 direction, PlaneData[] planes, bool[] boundDirections, VoxelEditorFrame parent, ref Vector3 faceNormal)
        {
            if (IsOutOfBounds(gridSpacePosition, boundDirections))
                return null;

            if (_grid.TryGetValue(gridSpacePosition, out VoxelEditorObject voxel))
                return voxel;

            float xSteps = (planes[0].Point - voxelSpacePosition.x) / direction.x;
            float ySteps = (planes[1].Point - voxelSpacePosition.y) / direction.y;
            float zSteps = (planes[2].Point - voxelSpacePosition.z) / direction.z;

            float minSteps = Mathf.Min(xSteps, ySteps, zSteps);

            int directionIndex = minSteps == xSteps ? 0 : minSteps == ySteps ? 1 : 2;
            faceNormal = planes[directionIndex].Normal;
            gridSpacePosition -= new Vector3Int((int)faceNormal.x, (int)faceNormal.y, (int)faceNormal.z);

            Vector3 newVoxelSpacePosition = voxelSpacePosition + direction * minSteps;
            float mirrorPosition = boundDirections[directionIndex] ? -0.05f : 0.05f;
            if (minSteps == xSteps)
                newVoxelSpacePosition.x = mirrorPosition;
            else if (minSteps == ySteps)
                newVoxelSpacePosition.y = mirrorPosition + 0.05f;
            else
                newVoxelSpacePosition.z = mirrorPosition;

            return GetIntersectedVoxel(gridSpacePosition, newVoxelSpacePosition, direction, planes, boundDirections, parent, ref faceNormal);
        }

        private bool IsOutOfBounds(Vector3Int position, bool[] boundDirections)
        {
            if ((boundDirections[0] && position.x > _max.x) || (!boundDirections[0] && position.x < _min.x) ||
                (boundDirections[1] && position.y > _max.y) || (!boundDirections[1] && position.y < _min.y) ||
                (boundDirections[2] && position.z > _max.z) || (!boundDirections[2] && position.z < _min.z))
                return true;

            return false;
        }

        internal Dictionary<Vector3Int, int> GetPositionToColor()
        {
            Dictionary<Vector3Int, int> positionToColor = new Dictionary<Vector3Int, int>();

            foreach (Vector3Int position in _grid.Keys)
            {
                positionToColor.Add(position, _grid[position].ColorIndex);
            }

            return positionToColor;
        }
    }
}
