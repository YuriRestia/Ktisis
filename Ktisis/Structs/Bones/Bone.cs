﻿using System.Numerics;
using System.Collections.Generic;

using ImGuizmoNET;

using Ktisis.Structs.Havok;
using Ktisis.Structs.Actor;

namespace Ktisis.Structs.Bones {
	public class Bone {
		public int Index;
		public short ParentId;
		public Transform Transform;
		public unsafe ActorModel* actor;
		public HkaBone HkaBone;
		public Quaternion RootRotation;
		public Quaternion ParentRelativeRotation;
		public bool IsRoot = false;
		public List<int> LinkedTo;

		public BoneList BoneList;

		// Constructor

		public unsafe Bone(BoneList bones, int index, ActorModel* model) {
			BoneList = bones;

			Index = index;
			ParentId = bones.Skeleton.ParentIndex[index];
			Transform = bones.Transforms[index];

			HkaBone = bones.Skeleton.Bones[index];

			UpdateTransform(bones);
			actor = model;
			RootRotation = actor->Rotation;
			if (this.GetParent() != null)
            {
				//RootRotation *= this.GetParent()!.Transform.Rotation;
				ParentRelativeRotation = Transform.Rotation * Quaternion.Inverse(this.GetParent()!.Transform.Rotation);
            }
            else
            {
				ParentRelativeRotation = Transform.Rotation;
			}
			LinkedTo = new List<int>();
		}

		// Update stored transform from matrix

		public void UpdateTransform(BoneList bones) {
			var t = bones.Transforms[Index];
			Transform = t;
		}

		// Apply stored transform

		public void ApplyTransform(BoneList bones) {
			if (ParentId == -1) return; // no
			bones.Transforms[Index] = Transform;
		}

		// Quaternion rotation

		public Vector3 Rotate(Quaternion quat) {
			var t = Transform.Position;
			return Vector3.Transform(new Vector3(t.X, t.Y, t.Z), quat);
		}

		// Transform bone

		public void TransformBone(Transform t) {
			Transform.Position += t.Position;
			// doesn't work, disable this for now.
			//Transform.Rotate *= t.Rotate;
			// also disable this while reworking BoneMod
			//Transform.Scale *= t.Scale;
		}

		public void TransformBone(Transform t, BoneList bones, bool parenting) {
			TransformBone(t);
			ApplyTransform(bones);
			if (parenting)
				TransformChildren(t, bones);
		}

		public void TransformBone(Transform t, List<BoneList> skeleton) {
			var children = new List<Bone>();

			var bones = skeleton[0];
			TransformBone(t, bones, false);
			bones.GetChildrenRecursive(this, ref children);
			foreach (var child in children) {
				child.TransformBone(t, bones, false);
				
				foreach (int index in child.LinkedTo) {
					var childBones = skeleton[index];
					childBones[0].TransformBone(t, childBones, true);
				}
			}
		}

		// Transform children

		public void TransformChildren(Transform t, BoneList bones) {
			var children = new List<Bone>();
			bones.GetChildrenRecursive(this, ref children);

			foreach (var child in children) {
				child.TransformBone(t, bones, false);
			}
		}

		// Get parent

		public Bone? GetParent() {
			return BoneList.GetParentOf(this);
		}
        // Get children
		public Quaternion GetWorldRotation(Quaternion result)
        {
			if (this.GetParent() != null)
			{
				result *= this.GetParent()!.GetWorldRotation(result);
			}
			else
			{
				result *= Transform.Rotation;
			}
			return result;
        } 

		// Get children

		public List<Bone> GetChildren() {
			return BoneList.GetChildrenDirect(this);
		}
    }
}
