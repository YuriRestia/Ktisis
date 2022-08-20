using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Logging;
using ImGuizmoNET;

using Ktisis.Helpers;
using Ktisis.Structs.Actor;

namespace Ktisis.Structs.Bones {
	public class BoneMod {
		public SharpDX.Matrix BoneMatrix;
		public SharpDX.Matrix DeltaMatrix;

		public Vector3 WorldPos;
		public Vector3 Rotation;
		public Vector3 Scale;

		public Quaternion RootRotation;
		public Vector3 RootRotationEul;
		public float ScaleModifier;
		public bool disableRead;
		public List<Bone> parentList;
		public BoneMod() {
			BoneMatrix = new SharpDX.Matrix();
			DeltaMatrix = new SharpDX.Matrix();

			WorldPos = new Vector3();
			Rotation = new Vector3();
			Scale = new Vector3(1.0f, 1.0f, 1.0f);

			RootRotation = new Quaternion();
			ScaleModifier = 1.0f;
			disableRead = false;
			parentList = new List<Bone>();
		}

		public unsafe void SnapshotBone(Bone bone, ActorModel* model, MODE mode = MODE.WORLD) {
			RootRotation = model->Rotation;
			ScaleModifier = model->Height;

			WorldPos = model->Position + bone.Rotate(RootRotation) * ScaleModifier;
			parentList.Clear();
			//Rotation = MathHelpers.ToEuler(mode == MODE.WORLD ? RootRotation : bone.GetParent()!.Transform.Rotate * bone.Transform.Rotate);
			if (mode == MODE.WORLD)
            {
                Rotation = MathHelpers.ToEuler(RootRotation);
            }
            else
            {
				var temp = bone.ParentRelativeRotation;
				//PluginLog.Log("Temp" + MathHelpers.ToEuler(temp));
				Rotation = MathHelpers.ToEuler(temp);
				Bone? parent = bone.GetParent()!;
                //Quaternion result = bone.Transform.Rotate;
                while (parent != null)
                {
					//parentList.Insert(0, parent);
					//               Quaternion parentRot = parent.Transform.Rotation;
					//parentRot = Quaternion.Inverse(parentRot);
					//PluginLog.Log("Parent Rotation" + MathHelpers.ToEuler(parent.ParentRelativeRotation));
					temp = temp * parent.ParentRelativeRotation;

					//PluginLog.Log("Temp" + MathHelpers.ToEuler(temp));
					//PluginLog.Log("RootRotation values: " + MathHelpers.ToEuler(Quaternion.Inverse(parentRot) * parentRot * bone.Transform.Rotation));
					//PluginLog.Log("RootRotation2 values: " + MathHelpers.ToEuler(bone.Transform.Rotation));
					//PluginLog.Log("Rotation values: " + Rotation.X + " " + Rotation.Y + " " + Rotation.Z);
					if (parent.GetParent()! != null)
                    {
						parent = parent.GetParent()!;
                    }
                    else
                    {
						parent = null;
                    }
				}
				Rotation = MathHelpers.ToEuler(temp);
				//Rotation = MathHelpers.ToEuler(bone.Transform.Rotation);
			}
			//PluginLog.Log("Selected Rotation" + Rotation);
            Scale = MathHelpers.ToVector3(bone.Transform.Scale);
			Scale = new Vector3(0.015f, 0.015f, 0.015f);
			ImGuizmo.RecomposeMatrixFromComponents(
				ref WorldPos.X,
				ref Rotation.X,
				ref Scale.X,
				ref BoneMatrix.M11
			);
		}

		public Transform GetDelta(Bone bone) {
			// Create vectors

			var translate = new Vector3();
			var rotation = new Vector3();
			var rotation2 = new Vector3();
			var scale = new Vector3();
			var scale2 = new Vector3();

			// Decompose into vectors
			/*
				This is a little hacky due to 2 separate bugs -
				Rotation via BoneMatrix suffers from gimbal lock
				Rotation via DeltaMatrix also results in unwanted translation
			*/

			var _ = new Vector3();

            ImGuizmo.DecomposeMatrixToComponents(
                ref BoneMatrix.M11,
                ref translate.X,
                ref rotation2.X,
                ref scale.X
            );
			ImGuizmo.DecomposeMatrixToComponents(
				ref DeltaMatrix.M11,
				ref _.X,
				ref rotation.X,
				ref scale2.X
			);
			//PluginLog.Log("Position values: " + translate.X + " " + translate.Y + " " + translate.Z);
			//PluginLog.Log("Scale values: " + scale.X + " " + scale.Y + " " + scale.Z);
			//ImGuizmo.DecomposeMatrixToComponents(
			//    ref DeltaMatrix.M11,
			//    ref _.X,
			//    ref rotation.X,
			//    ref scale.X
			//);

			// Convert to Transform

			var delta = new Transform();

			// Convert position

			//var inverse = Quaternion.Inverse(RootRotation);
			//delta.Position = Vector4.Transform(
			//    translate - WorldPos,
			//    inverse
			//) / ScaleModifier;

			//delta.Position.X = translate.X - WorldPos.X;
			//delta.Position.Y = translate.Y - WorldPos.Y;
			//delta.Position.Z = translate.Z - WorldPos.Z;
			//         delta.Scale.X = scale.X;
			//delta.Scale.Y = scale.Y;
			//delta.Scale.Z = scale.Z;
			// Attempt rotation

			var q = Quaternion.Inverse(Quaternion.Inverse(bone.GetParent()!.Transform.Rotation)) * MathHelpers.ToQuaternion(rotation2) ;
			//PluginLog.Log("Rotation values: " + rotation2.X + " " + rotation2.Y + " " + rotation2.Z);
			var j = MathHelpers.ToQuaternion(rotation2);
			delta.Rotation = j;
			//var j = MathHelpers.ToQuaternion(rotation2
			//);
			//delta.Rotation = Quaternion.Inverse(q) * MathHelpers.ToQuaternion(this.Rotation);
			//delta.Rotation = q * Quaternion.Inverse(Quaternion.Inverse(bone.GetParent()!.Transform.Rotation));
			//PluginLog.Log("Delta Rotation values: " + MathHelpers.ToEuler(delta.Rotation));
			// Update stored values

			//WorldPos = translate;
			//Rotation = rotation;
			//Scale = scale;

			// :D

			return delta;
		}
	}
}