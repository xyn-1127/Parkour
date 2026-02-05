# VR Locomotion Parkour

This repository contains my IGD301 locomotion implementation in the provided parkour scene.
The core  logic is implemented in `Assets/Scripts/LocomotionTechnique.cs`.

## Demo

- blog post: `https://xyn-1127.github.io/igd301-blog/posts/lecture-hw4/index.md`
- Video preview: `https://drive.google.com/file/d/1TcRcY6BeV1PBgIJC_9VAtvQiqFyYXvIN/preview`

## Implemented Locomotion Features

- Hold-to-engage gate using index trigger (`triggerHoldThreshold = 0.95`)
- Power-based arm-swing locomotion from controller motion
- Head-forward direction control (HMD yaw on horizontal plane)
- Rigidbody-based movement with acceleration/deceleration and speed clamp
- Button jump (`A`) with cooldown
- Respawn to current checkpoint (`B` or `Y`)

## Controls (Quest)

- Hold `Left/Right Index Trigger`: engage locomotion
- Swing both arms: move forward (faster swing -> higher speed)
- Turn head/body: change movement direction
- Press `A`: jump
- Press `B` or `Y`: respawn to current checkpoint

## Scenes and Scripts

### Main scenes

- `Assets/Scenes/ParkourChallenge.unity`

### Relevant scripts

- `Assets/Scripts/LocomotionTechnique.cs`: locomotion, jump, respawn, trigger handling

## Requirements

- Unity `6000.0.60f1`
- Meta XR All-in-One SDK (`com.meta.xr.sdk.all` `78.0.0`)
- Oculus XR Plugin (`com.unity.xr.oculus` `4.5.2`)
- Android build target (Quest)

