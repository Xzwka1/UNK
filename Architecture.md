# Architecture Overview (Current Codebase)

ไฟล์นี้สรุปและแสดงโครงสร้างความสัมพันธ์ของคลาสทั้งหมดในโปรเจกต์ปัจจุบัน (`Assets/Scripts/`)

---

## 1. System Interaction & Data Flow (ภาพรวมการไหลของข้อมูลและอีเวนต์)

```mermaid
flowchart TD
    subgraph Input_System ["Input System"]
        InputReader["PlayerInputReader (Static)<br/>• Read WASD, Jump, Dash, Grip"]
    end

    subgraph Player_GameObject ["Player GameObject (Assembled by PlayerSetup)"]
        Setup["PlayerSetup<br/>• Auto-wires Hitbox, Hurtbox, Visual"]
        Controller["PlayerController2D<br/>• Rigidbody2D Velocity-Driven<br/>• Jump / Air Jump / Dash / Wall-Climb<br/>• Coyote Time / Buffering / Corner Correction"]
        Stress["PlayerStress<br/>• 0..100% Stress Meter<br/>• Overloaded Event"]
        Respawn["PlayerRespawn<br/>• Fast Respawn Loop (0.2s)<br/>• Died & Respawned Events"]
        
        subgraph Child_Hurtbox ["Child GameObject: Hurtbox"]
            Hurtbox["PlayerHurtbox (Trigger)<br/>• 10x18 px Hazard Detector"]
        end
    end

    subgraph Environment_Level ["Environment & Level Mechanics"]
        Hazard["Hazard2D<br/>• Deadly Spikes / Killzones"]
        Fragile["FragilePlatform<br/>• Normal -> Crack -> Destroy -> Reform"]
    end

    subgraph Camera_System ["Camera System (Main Camera)"]
        CamCtrl["CameraController2D<br/>• SmoothDamp Follow<br/>• Lookahead & Deadzone<br/>• Room Bounds (Confine)"]
        CamEffects["CameraEffects (Singleton)<br/>• Micro / Heavy Screen Shake<br/>• Freeze Frame (Hit-Stop)"]
    end

    subgraph Prototyping ["Prototyping / Generator"]
        LevelGen["GreyboxLevelGenerator<br/>• Procedural Test Level Spawner"]
        Sprites["GreyboxSprites (Static)<br/>• Runtime 1x1 White Texture"]
    end

    %% Wiring & Data Flow
    InputReader -->|Polls Input State| Controller
    Setup -->|Configures Components| Controller
    Setup -->|Configures Components| Respawn
    Setup -->|Creates Child Trigger| Hurtbox
    Setup -->|Generates Visual Sprite| Sprites

    Controller -->|Applies Strain / Wall-jump Cost / Recovery| Stress
    Stress -->|100% Overloaded Event| Respawn
    Hurtbox -->|Detects Contact| Hazard
    Hurtbox -->|Kill Command| Respawn

    Respawn -->|Disables / Re-enables Movement| Controller
    Respawn -->|Resets Stress to 0%| Stress
    Respawn -->|Died Event: Triggers Heavy Shake & Freeze Frame| CamEffects

    Controller -.->|Stands on / Senses Ground| Fragile
    CamCtrl -->|Follows Position| Controller
    CamCtrl -->|Adds Shake Offset| CamEffects

    LevelGen -->|Spawns| Setup
    LevelGen -->|Sets Target| CamCtrl
    LevelGen -->|Spawns| Fragile
    LevelGen -->|Spawns| Hazard
```

---

## 2. Class Diagram (ความสัมพันธ์เชิงคลาสและเมธอด)

```mermaid
classDiagram
    direction TB

    class PlayerSetup {
        +pixelsPerUnit : float
        +hitboxPixels : Vector2
        +hurtboxPixels : Vector2
        +visualColor : Color
        -Apply(runtime : bool)
    }

    class PlayerController2D {
        +State : MoveState
        +IsGrounded : bool
        +IsGripping : bool
        +WallSide : int
        +Facing : int
        -SenseEnvironment()
        -TickNormal()
        -TickDashFreeze()
        -TickDashing()
        -TryJump()
        -BeginDash()
        -CornerCorrect()
    }

    class PlayerInputReader {
        <<static>>
        +Read() : PlayerInputState
    }

    class PlayerStress {
        +Current : float
        +Normalized : float
        +IsOverloaded : bool
        +Overloaded : Action
        +Add(amount : float)
        +AddRate(perSecond : float, dt : float)
        +Recover(dt : float)
        +ResetStress()
    }

    class PlayerRespawn {
        +IsDead : bool
        +spawnPoint : Transform
        +Died : Action
        +Respawned : Action
        +Kill()
        -RespawnRoutine()
    }

    class PlayerHurtbox {
        -OnTriggerEnter2D(other : Collider2D)
        -CheckHazard(other : Collider2D)
    }

    class Hazard2D {
        +Kill player on contact
    }

    class FragilePlatform {
        +State : PlatformState
        -crackDuration : float
        -destroyedDuration : float
        -reformDuration : float
        -TriggerCrack()
        -PlatformCycle()
    }

    class CameraController2D {
        +target : Transform
        +deadzoneSize : Vector2
        +lookaheadDistance : float
        +SetRoomBounds(min : Vector2, max : Vector2)
        +SnapToTarget()
        -FollowTarget()
        -ClampToRoom()
    }

    class CameraEffects {
        <<Singleton>>
        +Instance : CameraEffects
        +CurrentOffset : Vector3
        +MicroShake()
        +HeavyShake()
        +FreezeFrame(duration : float)
    }

    class GreyboxLevelGenerator {
        +GenerateRoom()
    }

    class GreyboxSprites {
        <<static>>
        +Square : Sprite
    }

    PlayerSetup ..> PlayerController2D : Configures & Requires
    PlayerSetup ..> PlayerRespawn : Configures & Requires
    PlayerSetup ..> PlayerHurtbox : Instantiates Hurtbox Child
    PlayerSetup ..> GreyboxSprites : Uses Square Sprite

    PlayerController2D ..> PlayerInputReader : Samples WASD/Space/Shift/Ctrl
    PlayerController2D --> PlayerStress : Manages rates & costs

    PlayerStress ..> PlayerRespawn : Event Overloaded
    PlayerHurtbox ..> PlayerRespawn : Calls Kill()
    PlayerHurtbox ..> Hazard2D : Collision Detection

    PlayerRespawn ..> PlayerController2D : Control lock & unlock
    PlayerRespawn ..> PlayerStress : Calls ResetStress()
    PlayerRespawn ..> CameraEffects : Subscribed to Died Event

    CameraController2D --> PlayerController2D : Target tracking
    CameraController2D ..> CameraEffects : Samples CurrentOffset

    FragilePlatform ..> PlayerController2D : Ground Interaction

    GreyboxLevelGenerator ..> PlayerSetup : Spawns Player
    GreyboxLevelGenerator ..> CameraController2D : Hooks Target
    GreyboxLevelGenerator ..> FragilePlatform : Spawns Platforms
    GreyboxLevelGenerator ..> Hazard2D : Spawns Hazards
```
