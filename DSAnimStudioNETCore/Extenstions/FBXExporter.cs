using System;
using System.Collections.Generic;
using Assimp.Unmanaged;
using Microsoft.Xna.Framework;
using SoulsAssetPipeline;
using SoulsAssetPipeline.Animation;

namespace DSAnimStudio.Extenstions;

using Assimp;
using NMatrix = System.Numerics.Matrix4x4;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

public class FBXExporter
{
    public static Scene ExportSingleAnimationToScene(NewHavokAnimation anim, Model currentPreviewModel)
    {
        NewAnimSkeleton_FLVER ModelSkel_Flver = currentPreviewModel.SkeletonFlver; //模型的骨骼
        var scene = new Scene();

        scene.RootNode = new Node("scene_root");

        scene.RootNode.Metadata["FrameRate"] = new Metadata.Entry(MetaDataType.Int32, 14);
        scene.RootNode.Metadata["TimeSpanStart"] = new Metadata.Entry(MetaDataType.UInt64, (ulong) 0);
        scene.RootNode.Metadata["TimeSpanStop"] = new Metadata.Entry(MetaDataType.UInt64, (ulong) 0);
        scene.RootNode.Metadata["CustomFrameRate"] =
            new Metadata.Entry(MetaDataType.Float, 1f / anim.FrameDuration);

        scene.RootNode.Metadata["FrontAxisSign"] = new Metadata.Entry(MetaDataType.Int32, -1);
        scene.RootNode.Metadata["OriginalUnitScaleFactor"] = new Metadata.Entry(MetaDataType.Int32, 100);
        scene.RootNode.Metadata["UnitScaleFactor"] = new Metadata.Entry(MetaDataType.Int32, 100);


        var a = new Assimp.Animation();
        a.DurationInTicks = anim.Duration * 30;
        a.TicksPerSecond = 30;
        a.Name = anim.Name;

        List<Node> animTrackNodes = new List<Node>();
        var hkxSkel = ModelSkel_Flver.HavokSkeletonThisIsMappedTo.HkxSkeleton;

        NodeAnimationChannel animTrack = null;
        Dictionary<int, int> dicFlverBoneIdx2ResultIdx = new Dictionary<int, int>();
        for (int i = 0; i < ModelSkel_Flver.FlverSkeleton.Count; i++)
        {
            NewAnimSkeleton_FLVER.FlverBoneInfo flvBone = ModelSkel_Flver.FlverSkeleton[i];

            if (animTrack == null)
            {
                animTrack = new NodeAnimationChannel();
            }

            int hkxBoneIndex = flvBone.HkxBoneIndex;

            if (hkxBoneIndex >= 0 && hkxBoneIndex < anim.data.TransformTrackIndexToHkxBoneMap.Length)
            {
                for (int f = 0; f < anim.FrameCount; f++)
                {
                    Matrix retMatrix;
                    {
                        var thisFrameTrans = anim.data.GetTransformOnFrameByBone(hkxBoneIndex, f, enableLooping: false);
                        retMatrix = thisFrameTrans.GetMatrix();
                        if (flvBone.ParentIndex < 0)
                        {
                            var itIndex = hkxBoneIndex;
                            do
                            {
                                var hkxParentIdx = hkxSkel[itIndex].ParentIndex;
                                if (hkxParentIdx < 0) break;
                                retMatrix *= anim.data.GetTransformOnFrameByBone(hkxParentIdx, f, enableLooping: false)
                                    .GetMatrix();
                                itIndex = hkxSkel[hkxParentIdx].ParentIndex;
                            } while (itIndex >= 0);
                        }
                    }

                    var t = new NewBlendableTransform(retMatrix.ToNumerics());
                    animTrack.PositionKeys.Add(new VectorKey(1.0 * f * anim.FrameDuration,
                        new Vector3D(t.Translation.X * -100, t.Translation.Y * 100, t.Translation.Z * 100)));
                    animTrack.ScalingKeys.Add(new VectorKey(1.0 * f * anim.FrameDuration,
                        new Vector3D(t.Scale.X, t.Scale.Y, t.Scale.Z)));
                    var q = t.Rotation;

                    q = SapMath.MirrorQuat(q);
                    animTrack.RotationKeys.Add(new QuaternionKey(1.0 * f * anim.FrameDuration,
                        new Quaternion(q.W, q.X, q.Y, q.Z)));


                    // var refTrans = NewBlendableTransform.Identity;
                    // if (flvBone.ParentIndex < 0)
                    // {
                    //     refTrans = new NewBlendableTransform(flvBone.ReferenceMatrix.ToNumerics());
                    // }
                    // else
                    // {
                    //     refTrans = new NewBlendableTransform(flvBone.ParentReferenceMatrix.ToNumerics());
                    // }
                    //
                    //
                    // animTrack.PositionKeys.Add(new VectorKey(1.0 * f * anim.FrameDuration,
                    //     new Vector3D(refTrans.Translation.X * -100, refTrans.Translation.Y * 100,
                    //         refTrans.Translation.Z * 100) +
                    //     new Vector3D(t.Translation.X * -100, t.Translation.Y * 100, t.Translation.Z * 100)));
                    // animTrack.ScalingKeys.Add(new VectorKey(1.0 * f * anim.FrameDuration,
                    //     new Vector3D(t.Scale.X, t.Scale.Y, t.Scale.Z)));
                    // var q = t.Rotation;
                    // var refq = refTrans.Rotation;
                    //
                    // q = SapMath.MirrorQuat(q);
                    // refq = SapMath.MirrorQuat(refq);
                    // animTrack.RotationKeys.Add(new QuaternionKey(1.0 * f * anim.FrameDuration,
                    //     new Quaternion(refq.W, refq.X, refq.Y, refq.Z) * new Quaternion(q.W, q.X, q.Y, q.Z)));
                }

                animTrack.NodeName = flvBone.Name;
                a.NodeAnimationChannels.Add(animTrack);

                dicFlverBoneIdx2ResultIdx.Add(i, animTrackNodes.Count);
                var fakeNode = new Node(flvBone.Name);
                animTrackNodes.Add(fakeNode);
                animTrack = null;
            }
        }

        List<Node> topLevelTrackNodes = new List<Node>();

        foreach (var kvP in dicFlverBoneIdx2ResultIdx)
        {
            var flvbone = ModelSkel_Flver.FlverSkeleton[kvP.Key];
            if (dicFlverBoneIdx2ResultIdx.TryGetValue(flvbone.ParentIndex, out int curParentResultIdx))
            {
                animTrackNodes[curParentResultIdx].Children.Add(animTrackNodes[kvP.Value]);
            }
            else
            {
                topLevelTrackNodes.Add(animTrackNodes[kvP.Value]);
            }
        }

        var actualRootNode = new Node("root");

        if (anim.RootMotion.Data != null)
        {
            animTrack = new NodeAnimationChannel();
            animTrack.NodeName = actualRootNode.Name;

            for (int f = 0; f < anim.FrameCount; f++)
            {
                var rootMotionOnFrame = anim.RootMotion.Data.GetSampleClamped(f * anim.FrameDuration);

                animTrack.PositionKeys.Add(new VectorKey(1.0 * f * anim.FrameDuration,
                    new Vector3D(rootMotionOnFrame.Z * -100, rootMotionOnFrame.Y * 100,
                        rootMotionOnFrame.X * 100)));

                var q = NQuaternion.CreateFromRotationMatrix(NMatrix.CreateRotationY(rootMotionOnFrame.W));
                animTrack.RotationKeys.Add(new QuaternionKey(1.0 * f * anim.FrameDuration,
                    new Quaternion(new Vector3D(0, -1, 0), (float) Math.PI) * new Quaternion(q.W, q.X, q.Y, q.Z)));
            }

            a.NodeAnimationChannels.Add(animTrack);
        }
        else
        {
            actualRootNode.Transform = Matrix4x4.FromRotationY((float) (Math.PI));
        }

        foreach (var t in topLevelTrackNodes)
        {
            actualRootNode.Children.Add(t);
        }

        scene.RootNode.Children.Add(actualRootNode);

        scene.Animations.Add(a);

        return scene;
    }

    public static Scene ExportSingleMeshToScene(Model currentPreviewModel)
    {
        // currentPreviewModel.ChrAsm.BodyMesh;
        var scene = new Scene();

        return scene;
    }


    public static string ExportToFile(Model currentPreviewModel, string filePath, string assimpFileFormatStr)
    {
        //currentPreviewModel.AnimContainer?.GetAllAnimations(); //获取所有当前角色的动画 
        var scene = ExportSingleAnimationToScene(currentPreviewModel.AnimContainer?.CurrentAnimation,
            currentPreviewModel);

        var Mesh = new Assimp.VertexWeight();
        using (var x = new AssimpContext())
        {
            if (!x.ExportFile(scene, filePath, assimpFileFormatStr)) return AssimpLibrary.Instance.GetErrorString();
            return string.Empty;
        }
    }
}