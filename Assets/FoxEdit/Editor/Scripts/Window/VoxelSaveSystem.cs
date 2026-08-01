using Autodesk.Fbx;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEditor.Animations;
using static FoxEdit.VoxelObject;

namespace FoxEdit
{
    internal class VoxelSaveSystem
    {
        internal static void Save(string meshName, string saveDirectory, VoxelRenderer voxelRenderer, VoxelPalette palette, int paletteIndex, List<VoxelEditorAnimation> animationList)
        {
            VoxelObject voxelObject = GetVoxelObject(voxelRenderer, meshName, saveDirectory, animationList);
            voxelObject = FillObject(voxelObject, animationList, palette, paletteIndex, saveDirectory, meshName);

            voxelRenderer.VoxelObject = voxelObject;
            EditorUtility.SetDirty(voxelRenderer);
            EditorUtility.SetDirty(voxelObject);
            AssetDatabase.SaveAssets();
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = voxelObject;
        }

        #region VoxelObjectCreation

        private static string GetAssetPath(string meshName, string saveDirectory, string extension)
        {

            string assetPath = null;
            if (saveDirectory == null || saveDirectory == "")
                assetPath = $"{meshName}.{extension}";
            else
                assetPath = $"{saveDirectory}/{meshName}.{extension}";

            return assetPath;
        }

        private static VoxelObject GetVoxelObject(VoxelRenderer voxelRenderer, string meshName, string saveDirectory, List<VoxelEditorAnimation> animationList)
        {
            VoxelObject voxelObject = AssetDatabase.LoadAssetAtPath<VoxelObject>(GetAssetPath(meshName, saveDirectory, "asset"));

            if (voxelObject == null)
                voxelObject = CreateVoxelObject(voxelRenderer, meshName, saveDirectory, animationList);

            return voxelObject;
        }

        private static VoxelObject CreateVoxelObject(VoxelRenderer voxelRenderer, string meshName, string saveDirectory, List<VoxelEditorAnimation> animationList)
        {
            string assetPath = GetAssetPath(meshName, saveDirectory, "asset");

            VoxelObject voxelObject = ScriptableObject.CreateInstance<VoxelObject>();
            AssetDatabase.CreateAsset(voxelObject, assetPath);
            voxelRenderer.VoxelObject = voxelObject;

            FoxEditSettings foxEditSettings = FoxEditSettings.GetSettings();

            Material staticOpaqueMaterialInstance = GameObject.Instantiate(foxEditSettings.Materials.staticOpaqueMaterial);
            AssetDatabase.CreateAsset(staticOpaqueMaterialInstance, GetAssetPath($"M_{meshName}_Static_Opaque", saveDirectory, "mat"));
            voxelObject.StaticOpaqueMaterial = staticOpaqueMaterialInstance;

            Material staticTransparentMaterialInstance = GameObject.Instantiate(foxEditSettings.Materials.staticTransparentMaterial);
            AssetDatabase.CreateAsset(staticTransparentMaterialInstance, GetAssetPath($"M_{meshName}_Static_Transparent", saveDirectory, "mat"));
            voxelObject.StaticTransparentMaterial = staticTransparentMaterialInstance;

            MeshRenderer staticRenderer = voxelRenderer.GetComponent<MeshRenderer>();
            staticRenderer.SetMaterials(new List<Material> { staticOpaqueMaterialInstance, staticTransparentMaterialInstance });

            AnimatorController animator = AnimatorController.CreateAnimatorControllerAtPath(GetAssetPath($"AC_{meshName}", saveDirectory, "controller"));
            voxelObject.AnimatorController = animator;

            EditorUtility.SetDirty(animator);
            EditorUtility.SetDirty(staticRenderer);
            EditorUtility.SetDirty(voxelRenderer);
            EditorUtility.SetDirty(voxelObject);
            AssetDatabase.SaveAssets();

            return voxelObject;
        }

        private static VoxelObject FillObject(VoxelObject voxelObject, List<VoxelEditorAnimation> editorAnimations, VoxelPalette palette, int paletteIndex, string saveDirectory, string meshName)
        {
            bool[] isColorTransparent = palette.GetColorOpacities();
            AnimationFrames[] animations = new AnimationFrames[editorAnimations.Count];

            for (int animationIndex = 0; animationIndex < editorAnimations.Count; animationIndex++)
            {
                List<int>[] startIndices = new List<int>[2];
                startIndices[0] = new List<int>(); //opaque
                startIndices[1] = new List<int>(); //transparent
                List<int>[] instanceCounts = new List<int>[2];
                instanceCounts[0] = new List<int>(); //opaque
                instanceCounts[1] = new List<int>(); //transparent
                List<EditorFrameVoxels> editorVoxels = new List<EditorFrameVoxels>();

                animations[animationIndex] = new AnimationFrames
                {
                    AnimName = editorAnimations[animationIndex].Name,
                    FrameCount = editorAnimations[animationIndex].FramesCount,
                    OpaqueMesh = new MeshData(),
                    HasOpaqueFaces = false,
                    TransparentMesh = new MeshData(),
                    HasTransparentFaces = false,
                    FrameDuration = editorAnimations[animationIndex].FrameDuration
                };

                List<Vector3Int> minBounds = new List<Vector3Int>();
                List<Vector3Int> maxBounds = new List<Vector3Int>();
                List<Vector3>[] animationVertices = new List<Vector3>[2];
                animationVertices[0] = new List<Vector3>(); //opaque
                animationVertices[1] = new List<Vector3>(); //transparent
                List<int>[] animationQuads = new List<int>[2];
                animationQuads[0] = new List<int>(); //opaque
                animationQuads[1] = new List<int>(); //transparent

                for (int frame = 0; frame < editorAnimations[animationIndex].FramesCount; frame++)
                {
                    VoxelObjectPackedFrameData packedData = editorAnimations[animationIndex][frame].GetPackedData();

                    EditorFrameVoxels editorVoxel = new VoxelObject.EditorFrameVoxels
                    {
                        VoxelPositions = packedData.VoxelPositionToColor.Keys.ToArray(),
                        ColorIndices = packedData.VoxelPositionToColor.Values.ToArray()
                    };
                    editorVoxels.Add(editorVoxel);
                    minBounds.Add(packedData.MinBounds);
                    maxBounds.Add(packedData.MaxBounds);

                    startIndices[0].Add(animationQuads[0].Count() / 5);
                    startIndices[1].Add(animationQuads[1].Count() / 5);
                    (int, int) newQuadsCount = GreedyMeshing(packedData, isColorTransparent, ref animationVertices, ref animationQuads, false);
                    instanceCounts[0].Add(newQuadsCount.Item1);
                    instanceCounts[1].Add(newQuadsCount.Item2);

                    if (frame == 0 && animationIndex == 0)
                    {
                        List<int> colors = packedData.VoxelPositionToColor.Values.GroupBy(color => color).Select(group => group.Key).ToList();
                        voxelObject.StaticMesh = CreateBinaryFBX(saveDirectory, $"SM_{meshName}", colors, animationVertices, animationQuads);
                    }
                }

                if (animationVertices[0].Count > 0)
                {
                    animations[animationIndex].HasOpaqueFaces = true;
                    animations[animationIndex].OpaqueMesh.InstanceStartIndices = startIndices[0].ToArray();
                    animations[animationIndex].OpaqueMesh.InstanceCount = instanceCounts[0].ToArray();
                    animations[animationIndex].OpaqueMesh.Vertices = animationVertices[0].ToArray();
                    animations[animationIndex].OpaqueMesh.Quads = animationQuads[0].ToArray();
                }
                else
                {
                    animations[animationIndex].OpaqueMesh.InstanceStartIndices = new int[0];
                    animations[animationIndex].OpaqueMesh.InstanceCount = new int[0];
                    animations[animationIndex].OpaqueMesh.Vertices = new Vector3[0];
                    animations[animationIndex].OpaqueMesh.Quads = new int[0];
                }

                if (animationVertices[1].Count > 0)
                {
                    animations[animationIndex].TransparentMesh.InstanceStartIndices = startIndices[1].ToArray();
                    animations[animationIndex].TransparentMesh.InstanceCount = instanceCounts[1].ToArray();
                    animations[animationIndex].TransparentMesh.Vertices = animationVertices[1].ToArray();
                    animations[animationIndex].TransparentMesh.Quads = animationQuads[1].ToArray();
                    animations[animationIndex].HasTransparentFaces = true;
                }
                else
                {
                    animations[animationIndex].TransparentMesh.InstanceStartIndices = new int[0];
                    animations[animationIndex].TransparentMesh.InstanceCount = new int[0];
                    animations[animationIndex].TransparentMesh.Vertices = new Vector3[0];
                    animations[animationIndex].TransparentMesh.Quads = new int[0];
                }

                animations[animationIndex].Bounds = CreateBounds(minBounds, maxBounds);
                animations[animationIndex].EditorVoxels = editorVoxels.ToArray();

                SetAnimation(voxelObject.AnimatorController, editorAnimations[animationIndex].Name, meshName, saveDirectory, animationIndex);
            }

            voxelObject.PaletteIndex = paletteIndex;
            voxelObject.Animations = animations;

            return voxelObject;
        }

        private static Bounds CreateBounds(List<Vector3Int> minBounds, List<Vector3Int> maxBounds)
        {
            Vector3Int min = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
            Vector3Int max = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);

            for (int i = 0; i < minBounds.Count; i++)
            {
                min.x = Mathf.Min(min.x, minBounds[i].x);
                min.y = Mathf.Min(min.y, minBounds[i].y);
                min.z = Mathf.Min(min.z, minBounds[i].z);

                max.x = Mathf.Max(max.x, maxBounds[i].x);
                max.y = Mathf.Max(max.y, maxBounds[i].y);
                max.z = Mathf.Max(max.z, maxBounds[i].z);
            }

            Bounds bounds = new Bounds();
            Vector3 center = (new Vector3(min.x + max.x + 1.0f, min.y + max.y + 1.0f, min.z + max.z + 1.0f) / 2.0f) * 0.1f;
            center.x -= 0.05f;
            center.z -= 0.05f;
            bounds.center = center;

            Vector3Int size = max - min;
            size.x = Mathf.Abs(size.x) + 1;
            size.y = Mathf.Abs(size.y) + 1;
            size.z = Mathf.Abs(size.z) + 1;

            bounds.extents = new Vector3((float)size.x / 2.0f, (float)size.y / 2.0f, (float)size.z / 2.0f) * 0.1f;

            return bounds;
        }

        private static void SetAnimation(RuntimeAnimatorController animator, string animationName, string meshName, string saveDirectory, int index)
        {
            string animationPath = GetAssetPath($"A_{meshName}_{animationName}", saveDirectory, "anim");
            AnimationClip clip = AssetDatabase.LoadAssetAtPath(animationPath, typeof(AnimationClip)) as AnimationClip;
            AnimationEvent animationEvent = null;

            if (clip == null)
            {
                clip = new AnimationClip();
                clip.name = $"A_{meshName}_{animationName}";
                AssetDatabase.CreateAsset(clip, animationPath);
                AnimatorState state = (animator as AnimatorController).layers[0].stateMachine.AddState(animationName);
                state.motion = clip;
            }

            if (clip.events.Length == 0)
            {
                animationEvent = new AnimationEvent();
                animationEvent.functionName = "SetAnimation";
                animationEvent.time = 0.0f;
            }
            else
            {
                animationEvent = clip.events[0];
            }

            animationEvent.intParameter = index;
            AnimationUtility.SetAnimationEvents(clip, new AnimationEvent[] { animationEvent });

            EditorUtility.SetDirty(animator);
            EditorUtility.SetDirty(clip);
        }

        #endregion VoxelObjectCreation

        #region GreedyMeshing

        internal static (int, int) GreedyMeshing(VoxelObjectPackedFrameData data, bool[] isColorTransparent, ref List<Vector3>[] vertices, ref List<int>[] quads, bool ignoreColors)
        {
            Vector3Int size = data.MinBounds - data.MaxBounds;
            size.x = Mathf.Abs(size.x) + 1;
            size.y = Mathf.Abs(size.y) + 1;
            size.z = Mathf.Abs(size.z) + 1;
            int[] colors = data.VoxelPositionToColor.Values.GroupBy(color => color).Select(group => group.Key).ToArray();

            BitArray[][] binaryMasks = FillBinaryMasks(data, isColorTransparent, size, ignoreColors);
            Dictionary<int, BitArray[][][]>[] greedyPlanes = FillGreedyPlanes(data, binaryMasks, colors, size, data.MinBounds, ignoreColors);
            Dictionary<int, List<Rect>[][]>[] orderedQuads = Combine(greedyPlanes, colors, ignoreColors);

            return CreateQuads(orderedQuads, size, data.MinBounds, ref vertices, ref quads);
        }

        private static BitArray[][] FillBinaryMasks(VoxelObjectPackedFrameData data, bool[] isColorTransparent, Vector3Int size, bool ignoreColors)
        {
            BitArray[] opaqueSlices = new BitArray[3];
            BitArray[] opaqueAndTransparentSlices = new BitArray[3];

            BitArray opaqueSlice;
            BitArray opaqueAndTransparentSlice;

            GetSlice(data, isColorTransparent, Vector3Int.right, size.x, Vector3Int.forward, size.z, Vector3Int.up, size.y, data.MinBounds, out opaqueSlice, out opaqueAndTransparentSlice, ignoreColors);
            opaqueSlices[0] = opaqueSlice;
            opaqueAndTransparentSlices[0] = opaqueAndTransparentSlice;
            GetSlice(data, isColorTransparent, Vector3Int.up, size.y, Vector3Int.right, size.x, Vector3Int.forward, size.z, data.MinBounds, out opaqueSlice, out opaqueAndTransparentSlice, ignoreColors);
            opaqueSlices[1] = opaqueSlice;
            opaqueAndTransparentSlices[1] = opaqueAndTransparentSlice;
            GetSlice(data, isColorTransparent, Vector3Int.forward, size.z, Vector3Int.right, size.x, Vector3Int.up, size.y, data.MinBounds, out opaqueSlice, out opaqueAndTransparentSlice, ignoreColors);
            opaqueSlices[2] = opaqueSlice;
            opaqueAndTransparentSlices[2] = opaqueAndTransparentSlice;

            BitArray[][] binaryMasks = new BitArray[2][];
            binaryMasks[0] = new BitArray[6]; //opaque
            binaryMasks[1] = new BitArray[6]; //transparent

            if (ignoreColors)
            {
                for (int axis = 0; axis < 3; axis++)
                {
                    BitArray baseSlice = opaqueAndTransparentSlices[axis];

                    binaryMasks[0][axis * 2] = (baseSlice.Clone() as BitArray).LeftShift(1).Not().And(baseSlice);
                    binaryMasks[0][axis * 2 + 1] = (baseSlice.Clone() as BitArray).RightShift(1).Not().And(baseSlice);
                }
            }
            else
            {
                for (int axis = 0; axis < 3; axis++)
                {
                    BitArray baseOpaqueSlice = opaqueSlices[axis];
                    BitArray baseOpaqueAndTransparentSlice = opaqueAndTransparentSlices[axis];

                    binaryMasks[0][axis * 2] = (baseOpaqueSlice.Clone() as BitArray).LeftShift(1).Not().And(baseOpaqueSlice);
                    binaryMasks[0][axis * 2 + 1] = (baseOpaqueSlice.Clone() as BitArray).RightShift(1).Not().And(baseOpaqueSlice);

                    binaryMasks[1][axis * 2] = (binaryMasks[0][axis * 2].Clone() as BitArray).Or((baseOpaqueAndTransparentSlice.Clone() as BitArray).LeftShift(1).Not().And(baseOpaqueAndTransparentSlice)).Xor(binaryMasks[0][axis * 2]);
                    binaryMasks[1][axis * 2 + 1] = (binaryMasks[0][axis * 2 + 1].Clone() as BitArray).Or((baseOpaqueAndTransparentSlice.Clone() as BitArray).RightShift(1).Not().And(baseOpaqueAndTransparentSlice)).Xor(binaryMasks[0][axis * 2 + 1]);
                }
            }

            return binaryMasks;
        }

        private static void GetSlice(VoxelObjectPackedFrameData data, bool[] isColorTransparent, Vector3Int sliceAxis, int axisSize, Vector3Int xAxis, int xSize, Vector3Int yAxis, int ySize, Vector3Int minBounds, out BitArray opaqueSlice, out BitArray opaqueAndTransparentSlice, bool ignoreColors)
        {
            int axisSizeWithPadding = axisSize + 1;
            int totalSize = xSize * ySize * axisSizeWithPadding;
            int sliceSize = xSize * axisSizeWithPadding;
            opaqueSlice = new BitArray(totalSize + 1, false);
            opaqueAndTransparentSlice = new BitArray(totalSize + 1, false);

            for (int i = 1; i < totalSize; i++)
            {
                int sliceIndex = i % axisSizeWithPadding;
                if (sliceIndex == 0)
                    continue;

                int x = (i / axisSizeWithPadding) % xSize;
                int y = i / sliceSize;

                Vector3Int position = sliceAxis * (sliceIndex - 1) + xAxis * x + yAxis * y + minBounds;
                int colorIndex;
                if (data.VoxelPositionToColor.TryGetValue(position, out colorIndex))
                {
                    if (!ignoreColors && !isColorTransparent[colorIndex])
                        opaqueSlice.Set(i, true);
                    opaqueAndTransparentSlice.Set(i, true);
                }
            }
        }

        private static Dictionary<int, BitArray[][][]>[] FillGreedyPlanes(VoxelObjectPackedFrameData data, BitArray[][] binaryMasks, int[] colors, Vector3Int size, Vector3Int minBounds, bool ignoreColors)
        {
            Dictionary<int, BitArray[][][]>[] greedyPlanes = new Dictionary<int, BitArray[][][]>[2];
            greedyPlanes[0] = new Dictionary<int, BitArray[][][]>(); //opaque
            greedyPlanes[1] = new Dictionary<int, BitArray[][][]>(); //transparent

            if (ignoreColors)
                colors = new int[1] { 0 };

            foreach (int color in colors)
            {
                greedyPlanes[0][color] = new BitArray[6][][];
                greedyPlanes[1][color] = new BitArray[6][][];
            }

            for (int axisIndex = 0; axisIndex < 6; axisIndex++)
            {
                bool isXAxis = (axisIndex == 0 || axisIndex == 1);
                bool isYAxis = (axisIndex == 2 || axisIndex == 3);

                int axisSize = isXAxis ? size.x : isYAxis ? size.y : size.z;
                int xSize = isXAxis ? size.z : isYAxis ? size.x : size.x;
                int ySize = isXAxis ? size.y : isYAxis ? size.z : size.y;
                int sliceSize = xSize * ySize;

                foreach (int color in colors)
                {
                    greedyPlanes[0][color][axisIndex] = new BitArray[axisSize][];
                    greedyPlanes[1][color][axisIndex] = new BitArray[axisSize][];
                }

                axisSize += 1;
                for (int sliceIndex = 0; sliceIndex < axisSize - 1; sliceIndex++)
                {
                    foreach (int color in colors)
                    {
                        greedyPlanes[0][color][axisIndex][sliceIndex] = new BitArray[ySize];
                        greedyPlanes[1][color][axisIndex][sliceIndex] = new BitArray[ySize];
                        for (int y = 0; y < ySize; y++)
                        {
                            greedyPlanes[0][color][axisIndex][sliceIndex][y] = new BitArray(xSize);
                            greedyPlanes[1][color][axisIndex][sliceIndex][y] = new BitArray(xSize);
                        }
                    }

                    for (int i = 0; i < sliceSize; i++)
                    {
                        bool hasOpaqueFace = binaryMasks[0][axisIndex].Get(i * axisSize + sliceIndex + 1);
                        bool hasTransparentFace = !ignoreColors && binaryMasks[1][axisIndex].Get(i * axisSize + sliceIndex + 1);

                        if (!hasOpaqueFace && !hasTransparentFace)
                            continue;

                        int x = i % xSize;
                        int y = i / xSize;

                        Vector3Int voxelPosition = new Vector3Int(
                            isXAxis ? sliceIndex : isYAxis ? x : x,
                            isXAxis ? y : isYAxis ? sliceIndex : y,
                            isXAxis ? x : isYAxis ? y : sliceIndex
                        );

                        int colorIndex;
                        if (ignoreColors)
                            greedyPlanes[0][0][axisIndex][sliceIndex][y].Set(x, true);
                        else if (data.VoxelPositionToColor.TryGetValue(voxelPosition + minBounds, out colorIndex))
                            greedyPlanes[hasOpaqueFace ? 0 : 1][colorIndex][axisIndex][sliceIndex][y].Set(x, true);
                    }
                }
            }

            return greedyPlanes;
        }

        private static Dictionary<int, List<Rect>[][]>[] Combine(Dictionary<int, BitArray[][][]>[] greedyPlanes, int[] colors, bool ignoreColors)
        {
            Dictionary<int, List<Rect>[][]>[] orderedQuads = new Dictionary<int, List<Rect>[][]>[2];
            orderedQuads[0] = new Dictionary<int, List<Rect>[][]>(); //opaque
            orderedQuads[1] = new Dictionary<int, List<Rect>[][]>(); //transparent

            if (ignoreColors)
                colors = new int[1] { 0 };

            foreach (var colorIndex in colors)
            {
                orderedQuads[0][colorIndex] = new List<Rect>[6][];
                orderedQuads[1][colorIndex] = new List<Rect>[6][];

                for (int axisIndex = 0; axisIndex < 6; axisIndex++)
                {
                    orderedQuads[0][colorIndex][axisIndex] = new List<Rect>[greedyPlanes[0][colorIndex][axisIndex].Length];
                    orderedQuads[1][colorIndex][axisIndex] = new List<Rect>[greedyPlanes[1][colorIndex][axisIndex].Length];

                    for (int sliceIndex = 0; sliceIndex < greedyPlanes[0][colorIndex][axisIndex].Length; sliceIndex++)
                    {
                        BinaryMerge(ref greedyPlanes, ref orderedQuads, colorIndex, axisIndex, sliceIndex, 0);
                        BinaryMerge(ref greedyPlanes, ref orderedQuads, colorIndex, axisIndex, sliceIndex, 1);
                    }
                }
            }

            return orderedQuads;
        }

        private static void BinaryMerge(ref Dictionary<int, BitArray[][][]>[] greedyPlanes, ref Dictionary<int, List<Rect>[][]>[] orderedQuads, int colorIndex, int axisIndex, int sliceIndex, int opacity)
        {
            orderedQuads[opacity][colorIndex][axisIndex][sliceIndex] = new List<Rect>();


            for (int y = 0; y < greedyPlanes[opacity][colorIndex][axisIndex][sliceIndex].Length; y++)
            {
                int x = 0;
                int length = greedyPlanes[opacity][colorIndex][axisIndex][sliceIndex][y].Length;
                while (x < length)
                {
                    BitArray clone = greedyPlanes[opacity][colorIndex][axisIndex][sliceIndex][y].Clone() as BitArray;
                    clone.RightShift(x);
                    int newOffset = TrailingCount(clone, false);
                    x += newOffset;
                    if (x >= length)
                        continue;

                    clone.RightShift(newOffset);
                    int width = Mathf.Max(1, TrailingCount(clone, true));
                    BitArray widthMask = new BitArray(length, false);
                    for (int w = 0; w < width; w++)
                    {
                        widthMask.Set(w, true);
                    }
                    BitArray mask = widthMask.Clone() as BitArray;
                    mask = mask.LeftShift(x);
                    mask = mask.Not();

                    int height = 1;
                    while (y + height < greedyPlanes[opacity][colorIndex][axisIndex][sliceIndex].Length)
                    {
                        BitArray nextRow = greedyPlanes[opacity][colorIndex][axisIndex][sliceIndex][y + height].Clone() as BitArray;
                        nextRow = nextRow.RightShift(x);
                        nextRow = nextRow.And(widthMask);
                        if (!BitEqual(nextRow, widthMask))
                            break;

                        greedyPlanes[opacity][colorIndex][axisIndex][sliceIndex][y + height] = greedyPlanes[opacity][colorIndex][axisIndex][sliceIndex][y + height].And(mask);
                        height += 1;
                    }

                    orderedQuads[opacity][colorIndex][axisIndex][sliceIndex].Add(new Rect(x, y, width, height));
                    x += width;
                }
            }
        }

        private static bool BitEqual(BitArray array1, BitArray array2)
        {
            for (int i = 0; i < array1.Length; i++)
            {
                if (array1[i] != array2[i])
                    return false;
            }
            return true;
        }

        private static int TrailingCount(BitArray array, bool target)
        {
            int length = array.Length;

            for (int i = 0; i < length; i++)
            {
                if (array[i] != target)
                    return i;
            }

            return length;
        }

        private static (int, int) CreateQuads(Dictionary<int, List<Rect>[][]>[] orderedQuads, Vector3Int size, Vector3Int minBounds, ref List<Vector3>[] vertices, ref List<int>[] quads)
        {
            (int, int) quadsCount = (0, 0); //opaque and transparent

            foreach (var color in orderedQuads[0].Keys)
            {
                for (int axis = 0; axis < 6; axis++)
                {
                    bool isXAxis = (axis == 0 || axis == 1);
                    bool isYAxis = (axis == 2 || axis == 3);

                    int axisSize = isXAxis ? size.x : isYAxis ? size.y : size.z;
                    int xSize = isXAxis ? size.z : isYAxis ? size.x : size.x;
                    int ySize = isXAxis ? size.y : isYAxis ? size.z : size.y;

                    for (int slice = 0; slice < axisSize; slice++)
                    {
                        quadsCount.Item1 += ParseVertices(ref vertices, ref quads, orderedQuads[0][color][axis][slice], axis, isXAxis, isYAxis, slice, color, minBounds, 0);
                        quadsCount.Item2 += ParseVertices(ref vertices, ref quads, orderedQuads[1][color][axis][slice], axis, isXAxis, isYAxis, slice, color, minBounds, 1);
                    }
                }
            }
            return quadsCount;
        }

        private static int ParseVertices(ref List<Vector3>[] vertices, ref List<int>[] quads, List<Rect> quadList, int axis, bool isXAxis, bool isYAxis, int slice, int color, Vector3Int minBounds, int opacity)
        {
            for (int i = 0; i < quadList.Count; i++)
            {
                Rect rect = quadList[i];
                int axisPosition = slice;
                int rightPosition = (int)rect.x;
                int upPosition = (int)rect.y;

                Vector3 voxelPosition = new Vector3
                (
                    isXAxis ? axisPosition : isYAxis ? rightPosition : rightPosition,
                    isXAxis ? upPosition : isYAxis ? axisPosition : upPosition,
                    isXAxis ? rightPosition : isYAxis ? upPosition : axisPosition
                ) + minBounds + new Vector3(axis == 1 ? 0.5f : -0.5f, axis == 3 ? 1.0f : 0.0f, axis == 5 ? 0.5f : -0.5f);
                voxelPosition *= 0.1f;

                int width = (int)rect.width;
                Vector3 widthVector = new Vector3
                (
                    isXAxis ? 0 : isYAxis ? width : width,
                    isXAxis ? 0 : isYAxis ? 0 : 0,
                    isXAxis ? width : isYAxis ? 0 : 0
                ) * 0.1f;

                int height = (int)rect.height;
                Vector3 heightVector = new Vector3
                (
                    isXAxis ? 0 : isYAxis ? 0 : 0,
                    isXAxis ? height : isYAxis ? 0 : height,
                    isXAxis ? 0 : isYAxis ? height : 0
                ) * 0.1f;

                if (axis == 0 || axis == 2 || axis == 5)
                {
                    AddVertex(voxelPosition, ref vertices, ref quads, opacity);

                    voxelPosition += heightVector;
                    AddVertex(voxelPosition, ref vertices, ref quads, opacity);

                    voxelPosition -= heightVector;
                    voxelPosition += widthVector;
                    AddVertex(voxelPosition, ref vertices, ref quads, opacity);

                    voxelPosition += heightVector;
                    AddVertex(voxelPosition, ref vertices, ref quads, opacity);
                }
                else
                {
                    voxelPosition += widthVector;
                    AddVertex(voxelPosition, ref vertices, ref quads, opacity);

                    voxelPosition += heightVector;
                    AddVertex(voxelPosition, ref vertices, ref quads, opacity);

                    voxelPosition -= widthVector;
                    voxelPosition -= heightVector;
                    AddVertex(voxelPosition, ref vertices, ref quads, opacity);

                    voxelPosition += heightVector;
                    AddVertex(voxelPosition, ref vertices, ref quads, opacity);
                }

                quads[opacity].Add(color);
            }

            return quadList.Count;
        }

        private static void AddVertex(Vector3 vertex, ref List<Vector3>[] vertices, ref List<int>[] quads, int opacity)
        {
            int index = vertices[opacity].IndexOf(vertex);
            if (index != -1)
            {
                quads[opacity].Add(index);
            }
            else
            {
                vertices[opacity].Add(vertex);
                quads[opacity].Add(vertices[opacity].Count - 1);
            }
        }

        private static void DebugGreedyPlanes(Dictionary<int, BitArray[][][]> greedyPlanes, int[] colors, VoxelPalette palette)
        {
            List<string> directions = new List<string>()
            {
                "Right",
                "Left",
                "Up",
                "Down",
                "Forward",
                "Back"
            };

            string result = "";
            foreach (int color in colors)
            {
                result += $"Color => {palette.Colors[color].Color}:\n\n";
                for (int axis = 0; axis < 6; axis++)
                {
                    result += $"  Axis => {directions[axis]}:\n\n";
                    int start = 0;
                    int end = greedyPlanes[color][axis].Length;
                    int direction = 1;
                    if (axis == 1 || axis == 3 || axis == 5)
                    {
                        start = end - 1;
                        end = -1;
                        direction = -1;
                    }
                    for (int slice = start; slice != end; slice += direction)
                    //for (int slice = 0; slice < greedyPlanes[color][axis].Length; slice++)
                    {
                        int start2 = 0;
                        int end2 = greedyPlanes[color][axis][slice].Length;
                        int direction2 = 1;
                        if (axis == 0 || axis == 1 || axis == 3 || axis == 4 || axis == 5)
                        {
                            start2 = end2 - 1;
                            end2 = -1;
                            direction2 = -1;
                        }
                        for (int up = start2; up != end2; up += direction2)
                        //for (int up = 0; up < greedyPlanes[color][axis][slice].Length; up++)
                        {
                            result += "    ";
                            int start3 = 0;
                            int end3 = greedyPlanes[color][axis][slice][up].Length;
                            int direction3 = 1;
                            if (axis == 0 || axis == 5)
                            {
                                start3 = end3 - 1;
                                end3 = -1;
                                direction3 = -1;
                            }
                            for (int right = start3; right != end3; right += direction3)
                            //for (int x = 0; x < greedyPlanes[color][axis][slice][y].Length; x++)
                            {
                                result += greedyPlanes[color][axis][slice][up][right] ? "O" : "_";
                            }
                            result += "\n";
                        }
                        result += "\n";
                    }
                    result += "\n";
                }
                result += "\n";
            }
            Debug.Log(result);
        }

        #endregion GreedyMeshing

        #region BinaryFbxCreation

        private static Mesh CreateBinaryFBX(string saveDirectory, string meshName, List<int> colors, List<Vector3>[] frameVertices, List<int>[] frameQuads)
        {
            string fbxPath = GetAssetPath(meshName, saveDirectory, "fbx");

            using (var fbxManager = FbxManager.Create())
            {
                FbxIOSettings fbxIOSettings = FbxIOSettings.Create(fbxManager, Globals.IOSROOT);
                fbxManager.SetIOSettings(fbxIOSettings);
                FbxExporter fbxExporter = FbxExporter.Create(fbxManager, "Exporter");
                int fileFormat = fbxManager.GetIOPluginRegistry().FindWriterIDByDescription("FBX ascii (*.fbx)");
                bool status = fbxExporter.Initialize(fbxPath, fileFormat, fbxIOSettings);

                if (!status)
                {
                    Debug.LogError($"Failed to initialize exporter, reason: {fbxExporter.GetStatus().GetErrorString()}");
                    return null;
                }

                FbxScene fbxScene = FbxScene.Create(fbxManager, "Voxel Scene");
                FbxDocumentInfo fbxSceneInfo = FbxDocumentInfo.Create(fbxManager, "Voxel Static Mesh");
                fbxSceneInfo.mTitle = meshName;
                fbxSceneInfo.mAuthor = "FoxEdit";
                fbxScene.SetSceneInfo(fbxSceneInfo);
                fbxScene.GetRootNode().AddChild(CreateFbxMesh(fbxManager, meshName, colors, frameVertices, frameQuads));
                fbxExporter.Export(fbxScene);
                fbxScene.Destroy();
                fbxExporter.Destroy();
            }

            AssetDatabase.Refresh();
            ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importBlendShapes = false;
            importer.importBlendShapeDeformPercent = false;
            importer.importVisibility = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.SaveAndReimport();

            GameObject meshGameObject = AssetDatabase.LoadAssetAtPath(fbxPath, typeof(GameObject)) as GameObject;
            return meshGameObject.GetComponent<MeshFilter>().sharedMesh;
        }

        private static FbxNode CreateFbxMesh(FbxManager fbxManager, string meshName, List<int> colors, List<Vector3>[] frameVertices, List<int>[] frameQuads)
        {
            bool hasOpaqueFaces = frameVertices[0].Count > 0;
            bool hasTransparentFaces = frameVertices[1].Count > 0;

            FbxMesh fbxMesh = FbxMesh.Create(fbxManager, meshName);
            fbxMesh.InitControlPoints(frameVertices[0].Count + frameVertices[1].Count);

            var normalElement = FbxLayerElementNormal.Create(fbxMesh, "Normals");
            normalElement.SetMappingMode(FbxLayerElement.EMappingMode.eByPolygon);
            normalElement.SetReferenceMode(FbxLayerElement.EReferenceMode.eIndexToDirect);
            var normalIndexArray = normalElement.GetIndexArray();
            var normalDirectArray = normalElement.GetDirectArray();

            List<Vector3> normals = new List<Vector3>(6)
            {
                new Vector3(1, 0, 0),
                new Vector3(-1, 0, 0),
                new Vector3(0, 1, 0),
                new Vector3(0, -1, 0),
                new Vector3(0, 0, 1),
                new Vector3(0, 0, -1)
            };
            foreach (Vector3 normal in normals)
            {
                normalDirectArray.Add(new FbxVector4(normal.x, normal.y, normal.z));
            }

            var uvElement = FbxLayerElementUV.Create(fbxMesh, "UVs");
            uvElement.SetMappingMode(FbxLayerElement.EMappingMode.eByPolygon);
            uvElement.SetReferenceMode(FbxLayerElement.EReferenceMode.eIndexToDirect);
            var uvIndexArray = uvElement.GetIndexArray();
            var uvDirectArray = uvElement.GetDirectArray();

            foreach (int color in colors)
            {
                uvDirectArray.Add(new FbxVector2(color, 0));
            }

            var materialElement = FbxLayerElementMaterial.Create(fbxMesh, "Materials");
            materialElement.SetMappingMode(FbxLayerElement.EMappingMode.eByPolygon);
            materialElement.SetReferenceMode(FbxLayerElement.EReferenceMode.eIndexToDirect);
            var materialArray = materialElement.GetIndexArray();

            int vertexIndex = 0;
            for (int opacity = 0; opacity < (hasOpaqueFaces && hasTransparentFaces ? 2 : 1); opacity++)
            {
                for (int i = 0; i < frameQuads[opacity].Count; i += 5)
                {
                    for (int y = 0; y < 4; y++)
                    {
                        int quadIndex = i + y;
                        Vector3 voxelPosition = frameVertices[opacity][frameQuads[opacity][quadIndex]];
                        voxelPosition *= 100.0f;

                        fbxMesh.SetControlPointAt(new FbxVector4(-voxelPosition.x, voxelPosition.y, voxelPosition.z), vertexIndex + y);
                    }

                    Vector3 faceNormal = GetFaceNormal(frameVertices[opacity][frameQuads[opacity][i]], frameVertices[opacity][frameQuads[opacity][i + 1]], frameVertices[opacity][frameQuads[opacity][i + 2]]);
                    int normalIndex = normals.IndexOf(faceNormal);
                    int colorIndex = colors.IndexOf(frameQuads[opacity][i + 4]);

                    fbxMesh.BeginPolygon(opacity);
                    fbxMesh.AddPolygon(vertexIndex);
                    fbxMesh.AddPolygon(vertexIndex + 1);
                    fbxMesh.AddPolygon(vertexIndex + 2);
                    fbxMesh.EndPolygon();
                    materialArray.Add(opacity);
                    normalIndexArray.Add(normalIndex);
                    uvIndexArray.Add(colorIndex);

                    fbxMesh.BeginPolygon(opacity);
                    fbxMesh.AddPolygon(vertexIndex + 1);
                    fbxMesh.AddPolygon(vertexIndex + 3);
                    fbxMesh.AddPolygon(vertexIndex + 2);
                    fbxMesh.EndPolygon();
                    materialArray.Add(opacity);
                    normalIndexArray.Add(normalIndex);
                    uvIndexArray.Add(colorIndex);

                    vertexIndex += 4;
                    normalIndex += 1;
                }
            }

            fbxMesh.GetLayer(0).SetNormals(normalElement);
            fbxMesh.GetLayer(0).SetUVs(uvElement);
            fbxMesh.GetLayer(0).SetMaterials(materialElement);

            FbxNode meshNode = FbxNode.Create(fbxManager, meshName);
            meshNode.LclTranslation.Set(new FbxDouble3(0.0, 0.0, 0.0));
            meshNode.LclRotation.Set(new FbxDouble3(0.0, 0.0, 0.0));
            meshNode.LclScaling.Set(new FbxDouble3(1.0, 1.0, 1.0));
            meshNode.SetNodeAttribute(fbxMesh);

            if (hasOpaqueFaces)
                meshNode.AddMaterial(FbxSurfacePhong.Create(fbxManager, "M_Opaque"));
            if (hasTransparentFaces)
                meshNode.AddMaterial(FbxSurfacePhong.Create(fbxManager, "M_Transparent"));

            return meshNode;
        }

        private static Vector3 GetFaceNormal(Vector4 point1, Vector4 point2, Vector4 point3)
        {
            Vector3 tangeant = point2 - point1;
            tangeant.x = -tangeant.x;
            Vector3 bitangeant = point3 - point1;
            bitangeant.x = -bitangeant.x;
            Vector3 normal = Vector3.Normalize(Vector3.Cross(tangeant, bitangeant));
            return new Vector3(normal.x, normal.y, normal.z);
        }

        #endregion BinaryFbxCreation
    }
}
