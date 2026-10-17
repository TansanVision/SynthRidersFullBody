using SynthRidersFullBody;
using UnityEngine;
using UnityEngine.Rendering;

var checks = 0;
var headCamera = new Camera();
var output = new Camera { fieldOfView = 70f };
output.transform.localPosition = new(5, 4, 3);
output.transform.localRotation = Quaternion.LookRotation(new(1, 0, 0), Vector3.up);
var originalPosition = output.transform.localPosition;
var originalRotation = output.transform.localRotation;
var avatar = new Animator();
avatar.Bones[HumanBodyBones.Head] = new() { localPosition = new(0, 1.6f, 0) };
avatar.Bones[HumanBodyBones.Hips] = new() { localPosition = new(0, 1f, 0) };
avatar.Bones[HumanBodyBones.Chest] = new() { localPosition = new(0, 1.25f, 0) };
avatar.Bones[HumanBodyBones.LeftUpperArm] = new() { localPosition = new(-.25f, 1.4f, 0) };
avatar.Bones[HumanBodyBones.RightUpperArm] = new() { localPosition = new(.25f, 1.4f, 0) };
CaptureCameraResolver.Selected = output;
var otherListenerCalls = 0;
Camera.CameraCallback listener = (Action<Camera>)(_ => otherListenerCalls++);
Camera.onPreCull += listener;
using var controller = new MenuBustCamera();
controller.Initialize();
controller.Update(headCamera, avatar, true, false);
Camera.onPreCull?.Invoke(headCamera);
Check(headCamera.fieldOfView == 60f && headCamera.transform.position.Equals(default(Vector3)),
    "Headset camera was modified.");
Camera.onPreCull?.Invoke(output);
var lockedPosition = output.transform.position;
Check(output.fieldOfView == 35f && !lockedPosition.Equals(originalPosition), "Built-in render was not framed.");
// Duplicate callbacks must not replace the saved game pose with the temporary portrait pose.
RenderPipelineManager.beginCameraRendering?.Invoke(default, output);
Camera.onPostRender?.Invoke(output);
CheckRestored();
RenderPipelineManager.endCameraRendering?.Invoke(default, output);
CheckRestored();

avatar.Bones[HumanBodyBones.Head].localPosition = new(.3f, 1.7f, .2f);
controller.Update(headCamera, avatar, true, false);
RenderPipelineManager.beginCameraRendering?.Invoke(default, output);
Check(output.transform.position.Equals(lockedPosition), "Head motion moved the fixed camera.");
RenderPipelineManager.endCameraRendering?.Invoke(default, output);
CheckRestored();

// Starting gameplay invalidates a cached camera even before the next MOD Update.
MenuCameraContext.Open = false;
Camera.onPreCull?.Invoke(output);
CheckRestored();
MenuCameraContext.Open = true;
controller.Update(headCamera, avatar, true, true);
Camera.onPreCull?.Invoke(output);
CheckRestored();
Check(controller.Status == "Menu bust shot: waiting for avatar pose",
    "Calibration recovery was reported as an unrelated menu state.");
// A loading/recovery pose must not become the fixed composition.
avatar.Bones[HumanBodyBones.Head].localPosition = new(2, 2.5f, 2);
controller.Update(headCamera, avatar, true, true);
Camera.onPreCull?.Invoke(output);
Check(!controller.TryGetPosition(output, out _), "Camera guide retained a composition while calibration was restoring.");
CheckRestored();
avatar.Bones[HumanBodyBones.Head].localPosition = new(.3f, 1.7f, .2f);
controller.Update(headCamera, avatar, true, false);
Camera.onPreCull?.Invoke(output);
Check(output.transform.position.Equals(lockedPosition) == false && output.fieldOfView == 35 &&
    output.transform.position.x < 1f && output.transform.position.y < 2f,
    "Camera did not reframe from the restored avatar pose.");
controller.Update(headCamera, avatar, false, false);
CheckRestored();
Camera.onPostRender?.Invoke(output);
CheckRestored();

controller.Update(headCamera, avatar, true, false);
CaptureCameraResolver.Selected = headCamera; // Game switches mode after LateUpdate.
Camera.onPreCull?.Invoke(output);
CheckRestored();
CaptureCameraResolver.Selected = output;
Time.unscaledTime += 1;
controller.Update(headCamera, avatar, true, false);
Camera.onPreCull?.Invoke(output);
controller.ResetFrame(); // Scene change during a render.
CheckRestored();

controller.Update(headCamera, avatar, true, false);
Camera.onPreCull?.Invoke(output);
controller.Update(headCamera, avatar, true, false); // Missing end callback / aborted render.
CheckRestored();
output.transform.ThrowOnSet = true;
Camera.onPreCull?.Invoke(output);
Check(controller.Error != null, "Render error was not reported.");
CheckRestored();
output.transform.ThrowOnSet = false;
Time.unscaledTime += 6;
controller.Update(headCamera, avatar, true, false);
Camera.onPreCull?.Invoke(output);
Check(output.fieldOfView == 35f && controller.Error == null, "Camera did not recover after a render error.");
controller.Dispose();
CheckRestored();
var before = otherListenerCalls;
Camera.onPreCull?.Invoke(output);
Check(otherListenerCalls == before + 1, "Disposal removed another listener.");
CheckRestored();
Camera.onPreCull -= listener;
Console.WriteLine($"Menu camera render/restore tests passed ({checks} checks).");

void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
void CheckRestored() => Check(output.transform.localPosition.Equals(originalPosition) &&
    output.transform.localRotation.Equals(originalRotation) && output.fieldOfView == 70f,
    "Original camera position, rotation or FOV was not restored.");
