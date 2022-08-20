using System;
using System.Numerics;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using ImGuiNET;
using ImGuizmoNET;

using Dalamud.Game.Gui;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Types;

using FFXIVClientStructs.FFXIV.Client.Game.Control;

using Ktisis.Structs;
using Ktisis.Structs.Actor;
using Ktisis.Structs.Bones;
using Ktisis.Structs.FFXIV;
using Ktisis.Helpers;
using Dalamud.Logging;

namespace Ktisis.Overlay {
	public sealed class SkeletonEditor {
		private Ktisis Plugin;
		private GameGui Gui;
		private ObjectTable ObjectTable;

		public bool Visible = true;

		public GameObject? Subject;
		public List<BoneList>? Skeleton;

		public BoneSelector BoneSelector;
		public BoneMod BoneMod;
		public bool enableWrite;
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		internal delegate IntPtr GetMatrixDelegate();
		internal GetMatrixDelegate GetMatrix;

		float[] cameraView = {
			1.0f, 0.0f, 0.0f, 0.0f,
			0.0f, 1.0f, 0.0f, 0.0f,
			0.0f, 0.0f, 1.0f, 0.0f,
			0.0f, 0.0f, 0.0f, 1.0f
		};

		// Controls

		public OPERATION GizmoOp = OPERATION.UNIVERSAL;
		public MODE Gizmode = MODE.LOCAL; // TODO: Improve this.

		// Constructor

		public unsafe SkeletonEditor(Ktisis plugin, GameObject? subject) {
			Plugin = plugin;
			Gui = plugin.GameGui;
			ObjectTable = plugin.ObjectTable;

			Subject = subject;

			BoneSelector = new BoneSelector();
			BoneMod = new BoneMod();

			var matrixAddr = plugin.SigScanner.ScanText("E8 ?? ?? ?? ?? 48 8D 4C 24 ?? 48 89 4c 24 ?? 4C 8D 4D ?? 4C 8D 44 24 ??");
			GetMatrix = Marshal.GetDelegateForFunctionPointer<GetMatrixDelegate>(matrixAddr);
		}

		// Toggle visibility

		public void Show() {
			Visible = true;
		}

		public void Hide() {
			Visible = false;
		}

		// Get ActorModel

		public unsafe ActorModel* GetSubjectModel() {
			return ((Actor*)Subject?.Address)->Model;
		}

		// Bone selection

		public unsafe void SelectBone(Bone bone, BoneList bones) {
			var model = GetSubjectModel();
			if (model == null) return;

			BoneSelector.Current = (bones.Id, bone.Index);

			enableWrite = false;
			BoneMod.SnapshotBone(bone, model, Gizmode);
			enableWrite = true;
		}

		// Build skeleton

		public unsafe void BuildSkeleton() {
			Skeleton = new List<BoneList>();

			var model = GetSubjectModel();
			if (model == null)
				return;

			var linkList = new Dictionary<string, List<int>>(); // name : [index]

			// Create BoneLists

			var list = *model->HkaIndex;
			for (int i = 0; i < list.Count; i++) {
				var index = list[i];
				if (index.Pose == null)
					continue;

				var bones = new BoneList(i, index.Pose, model);

				var first = bones[0];
				first.IsRoot = true;

				// Is linked
				if (i > 0) {
					var firstName = first.HkaBone.Name!;

					if (!linkList.ContainsKey(firstName))
						linkList.Add(firstName, new List<int>());
					linkList[firstName].Add(i);
				}

				Skeleton.Add(bones);
			}

			// Set LinkedTo

			foreach (Bone bone in Skeleton[0]) {
				var name = bone.HkaBone.Name!;
				if (linkList.ContainsKey(name))
					bone.LinkedTo = linkList[name];
			}
		}

		// Draw

		public unsafe void Draw(ImDrawListPtr draw) {
			if (!Visible || !Plugin.Configuration.ShowSkeleton)
				return;

			if (!Plugin.IsInGpose())
				return;

			var tarSys = TargetSystem.Instance();
			if (tarSys == null)
				return;

			var target = ObjectTable.CreateObjectReference((IntPtr)(tarSys->GPoseTarget));
			if (target == null || Subject == null || Subject.Address != target.Address) {
				Subject = target;
				if (Subject != null)
					BuildSkeleton();
			}
			if (Subject == null)
				return;
			if (Skeleton == null)
				return;

			var model = GetSubjectModel();
			if (model == null)
				return;

			var cam = CameraManager.Instance()->Camera;
			if (cam == null)
				return;

			var hoveredBones = new List<(int ListId, int Index)>();


			foreach (BoneList bones in Skeleton) {
				foreach (Bone bone in bones) {
					if (bone.IsRoot)
						continue;

					var pair = (bones.Id, bone.Index);

					var worldPos = model->Position + bone.Rotate(model->Rotation) * model->Height;
					Gui.WorldToScreen(worldPos, out var pos);

					if (Plugin.Configuration.DrawLinesOnSkeleton) {
						if (bone.ParentId > 0) { // Lines
							var parent = bone.GetParent()!;
							var parentPos = model->Position + parent.Rotate(model->Rotation) * model->Height;

							Gui.WorldToScreen(parentPos, out var pPos);
							draw.AddLine(pos, pPos, 0x90ffffff);
						}
					}

					if (pair == BoneSelector.Current && enableWrite) { // Gizmo

						var io = ImGui.GetIO();
						var wp = ImGui.GetWindowPos();

						ImGuizmo.SetOrthographic(false);
						var matrix = (WorldMatrix*)GetMatrix();
						if (matrix == null)
							return;

						ImGuizmo.BeginFrame();
						ImGuizmo.SetDrawlist();
						ImGuizmo.SetRect(wp.X, wp.Y, io.DisplaySize.X, io.DisplaySize.Y);

						ImGuizmo.AllowAxisFlip(Plugin.Configuration.AllowAxisFlip);

						ImGuizmo.Manipulate(
							ref matrix->Projection.M11,
							ref cameraView[0],
							GizmoOp,
							Gizmode,
							ref BoneMod.BoneMatrix.M11,
							ref BoneMod.DeltaMatrix.M11
						);

						ImGuizmo.DrawCubes(
							ref matrix->Projection.M11,
							ref cameraView[0],
							ref BoneMod.BoneMatrix.M11,
							1
						);

						// TODO: Streamline this.
						//var Rotation = new Vector3();
						//Quaternion result = bone.Transform.Rotate;
						//if (parent != null)
						//{
						//	Quaternion parentRot = parent.Transform.Rotation;
						//	parentRot = Quaternion.Inverse(parentRot);
						//	Rotation = MathHelpers.ToEuler(parentRot * bone.Transform.Rotation);
						//}
						//else
						//{
						//	Rotation = MathHelpers.ToEuler(bone.Transform.Rotation);
						//}
						
						//var delta = BoneMod.GetDelta(bone);
						var translate = new Vector3();
						var translate2 = new Vector3();
						var rotation = new Vector3();
						var rotation2 = new Vector3();
						var scale = new Vector3();
						var scale2 = new Vector3();
                        ImGuizmo.DecomposeMatrixToComponents(
                            ref BoneMod.BoneMatrix.M11,
                            ref translate.X,
                            ref rotation.X,
                            ref scale.X
                        );
                        ImGuizmo.DecomposeMatrixToComponents(
							ref BoneMod.DeltaMatrix.M11,
							ref translate2.X,
							ref rotation2.X,
							ref scale2.X
						);
						Bone parent = bone.GetParent()!;
						var temp = bone.ParentRelativeRotation;
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
						PluginLog.Log("bone values: " + MathHelpers.ToEuler(temp));
						PluginLog.Log("cursed values: " + MathHelpers.ToEuler(MathHelpers.ToQuaternion(rotation)));
						PluginLog.Log("cursed values 2 " + rotation);
						//if (!MathHelpers.IsApproximately(temp, MathHelpers.ToQuaternion(rotation), 0.001f))
      //                  {
						//	PluginLog.Log("bone values: " + MathHelpers.ToEuler(temp));
						//	PluginLog.Log("cursed values: " + MathHelpers.ToEuler(MathHelpers.ToQuaternion(rotation)));
						//	//bone.Transform.Rotation = model->Rotation * MathHelpers.ToQuaternion(rotation);
						//	//bone.TransformBone(bone.Transform, Skeleton);
						//	//ImGuizmo.RecomposeMatrixFromComponents(
						//	//                             ref translate.X,
						//	//                             ref rotation.X,
						//	//                             ref scale.X,
						//	//                             ref BoneMod.BoneMatrix.M11
						//	//                         );
						//}
						//if (!MathHelpers.IsApproximately(bone.Transform.Rotation,  parent.Transform.Rotation * MathHelpers.ToQuaternion(rotation), 0.01f))
						//if (!MathHelpers.IsApproximately(bone.Transform.Rotation, bone.Transform.Rotation * MathHelpers.ToQuaternion(rotation2), 0.0001f))
						//{
						//	Quaternion result;
						//	//if (BoneMod.parentList.Count > 0)
						//	//{
						//	//	for (int i = 0; i < BoneMod.parentList.Count; i++)
						//	//	{
						//	//		if(i == 0)
						// //                             {
						//	//			result = BoneMod.parentList[0].Transform.Rotation;
						// //                             }
						// //                             else
						// //                             {
						//	//			result *= 
						// //                             }
						//	//	}
						//	//}
						//	bone.Transform.Rotation = bone.Transform.Rotation * MathHelpers.ToQuaternion(rotation2);
						//	//bone.TransformBone(bone.Transform, Skeleton);
						//	//Quaternion result = bone.Transform.Rotate;
						//	Quaternion outRot = bone.Transform.Rotation;
						//	PluginLog.Log("Init values: " + MathHelpers.ToEuler(outRot));
						//	Quaternion testRot = MathHelpers.ToQuaternion(rotation);
						//	PluginLog.Log("Init testRot values: " + MathHelpers.ToEuler(testRot));
						//	Quaternion parentRot;
						//	//while (parent != null)
						//	//{
						//	//	parentRot = parent.Transform.Rotation;
						//	//	testRot = parentRot * testRot;
						//	//	parentRot = Quaternion.Inverse(parentRot);

						//	//	outRot = parentRot * outRot;
						//	//	//PluginLog.Log("RootRotation values: " + MathHelpers.ToEuler(Quaternion.Inverse(parentRot) * parentRot * bone.Transform.Rotation));
						//	//	//PluginLog.Log("RootRotation2 values: " + MathHelpers.ToEuler(bone.Transform.Rotation));
						//	//	//PluginLog.Log("Rotation values: " + Rotation.X + " " + Rotation.Y + " " + Rotation.Z);
						//	//	if (parent.GetParent()! != null)
						//	//	{
						//	//		parent = parent.GetParent()!;
						//	//	}
						//	//	else
						//	//	{
						//	//		parent = null;
						//	//	}
						//	//}
						//	var result = MathHelpers.ToEuler(outRot);
						//	PluginLog.Log("Result values: " + MathHelpers.ToEuler(outRot));
						//	PluginLog.Log("Test values: " + MathHelpers.ToEuler(testRot));
						//	//PluginLog.Log("BoneBeforeChange " + MathHelpers.ToEuler(bone.Transform.Rotation));
						//	//PluginLog.Log("Rotation2 " + rotation2);

						//	//PluginLog.Log("BoneAfterChange " + MathHelpers.ToEuler(bone.Transform.Rotation));
						//	//var holder = MathHelpers.ToEuler(bone.Transform.Rotation * Quaternion.Inverse((Quaternion.Inverse(model->Rotation))));
						//	//PluginLog.Log("Holder: " + MathHelpers.ToEuler(bone.Transform.Rotation));
						//	//Quaternion parentRot = parent.Transform.Rotation;
						//	//parentRot = Quaternion.Inverse(parentRot);
						//	//rotation2 = MathHelpers.ToEuler(parentRot * bone.Transform.Rotation);
						//	//ImGuizmo.RecomposeMatrixFromComponents(
						//	//	ref translate.X,
						//	//	ref result.X,
						//	//	ref scale.X,
						//	//	ref BoneMod.BoneMatrix.M11
						//	//);
						//}
						//var changed = false;
						//                  if (!(MathHelpers.IsApproximately(bone.Transform.Rotation, Quaternion.Inverse(model->Rotation) * delta.Rotation)))
						//                  {
						//	BoneMod.disableRead = true;
						//	PluginLog.Log("BoneBeforeChange " + MathHelpers.ToEuler(bone.Transform.Rotation));
						//	bone.Transform.Rotation = Quaternion.Inverse(model->Rotation) * delta.Rotation;
						//	PluginLog.Log("BoneAfterChange " + MathHelpers.ToEuler(bone.Transform.Rotation));
						//	//Quaternion result = bone.Transform.Rotate;
						//	//if (parent != null)
						//	//{
						//	//	Quaternion parentRot = parent.Transform.Rotation;
						//	//	parentRot = Quaternion.Inverse(parentRot);
						//	//	BoneMod.Rotation = MathHelpers.ToEuler(parentRot * bone.Transform.Rotation);
						//	//}
						//	//else
						//	//{
						//	//	BoneMod.Rotation = MathHelpers.ToEuler(bone.Transform.Rotation);
						//	//}
						//	changed = true;
						//                  }
						//Quaternion result = bone.Transform.Rotation;

						//PluginLog.Log("Rotation values bone: " + MathHelpers.ToEuler(bone.GetWorldRotation(result)));
						//if (!(MathHelpers.IsApproximately(bone.Transform.Position, delta.Position)))
						//{
						//	bone.Transform.Position = delta.Position;
						//	changed = true;
						//}
						//if (!(MathHelpers.IsApproximately(bone.Transform.Scale, delta.Scale)))
						//{
						//	bone.Transform.Scale = delta.Scale;
						//	changed = true;
						//}
						//bone.Transform.Rotate = delta.Rotate;
						//bone.TransformBone(delta, Skeleton);
						//if (changed)
						//{
						//PluginLog.Log("Position values bone: " + bone.Transform.Position.X + " " + bone.Transform.Position.Y + " " + bone.Transform.Position.Z);
						//PluginLog.Log("Scale values bone: " + bone.Transform.Scale.X + " " + bone.Transform.Scale.Y + " " + bone.Transform.Scale.Z);
						//bone.TransformBone(bone.Transform, Skeleton);
						//BoneMod.disableRead = false;
						//ImGuizmo.RecomposeMatrixFromComponents(
						//	ref BoneMod.WorldPos.X,
						//	ref BoneMod.Rotation.X,
						//	ref BoneMod.Scale.X,
						//	ref BoneMod.BoneMatrix.M11
						//);
						//}

					}
					else { // Dot
						var radius = Math.Max(3.0f, 10.0f - cam->Distance);

						var area = new Vector2(radius, radius);
						var rectMin = pos - area;
						var rectMax = pos + area;

						var hovered = ImGui.IsMouseHoveringRect(rectMin, rectMax);
						if (hovered)
							hoveredBones.Add(pair);

						draw.AddCircleFilled(pos, Math.Max(2.0f, 8.0f - cam->Distance), hovered ? 0xffffffff : 0x90ffffff, 100);
					}
				}

				//break;
			}

			if (hoveredBones.Count > 0)
				BoneSelector.Draw(this, hoveredBones);
		}

	}
}