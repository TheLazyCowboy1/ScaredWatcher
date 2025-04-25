using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Security.Permissions;
using UnityEngine;
using RWCustom;
using BepInEx;
using Watcher;

#pragma warning disable CS0618

[module: UnverifiableCode]
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]

namespace ScaredWatcher;

[BepInPlugin(MOD_ID, MOD_NAME, MOD_VERSION)]
public partial class Plugin : BaseUnityPlugin
{
    public const string MOD_ID = "LazyCowboy.ScaredWatcher",
        MOD_NAME = "Traumatized Watcher Behavior",
        MOD_VERSION = "0.0.8";


    private static ConfigOptions Options;

    public Plugin()
    {
        try
        {
            Options = new ConfigOptions();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
            throw;
        }
    }
    private void OnEnable()
    {
        On.RainWorld.OnModsInit += RainWorldOnOnModsInit;
    }
    private void OnDisable()
    {
        On.RainWorld.OnModsInit -= RainWorldOnOnModsInit;
        if (IsInit)
        {
            On.Player.checkInput -= Player_checkInput;
            On.Room.PlaySound_SoundID_PositionedSoundEmitter_bool_float_float_bool -= Room_PlaySound_Positioned;
            IsInit = false;
        }
    }

    private bool IsInit;
    private void RainWorldOnOnModsInit(On.RainWorld.orig_OnModsInit orig, RainWorld self)
    {
        orig(self);
        try
        {
            if (IsInit) return;

            On.Player.checkInput += Player_checkInput;
            On.Room.PlaySound_SoundID_PositionedSoundEmitter_bool_float_float_bool += Room_PlaySound_Positioned;

            Options.SetupSlugcatConfigs(); //done here in case config menu is never opened
            
            MachineConnector.SetRegisteredOI(MOD_ID, Options);
            IsInit = true;

            Logger.LogDebug("Applied hooks");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
            throw;
        }
    }

    private void Room_PlaySound_Positioned(On.Room.orig_PlaySound_SoundID_PositionedSoundEmitter_bool_float_float_bool orig, Room self, SoundID soundId, PositionedSoundEmitter em, bool loop, float vol, float pitch, bool randomStartPosition)
    {
        orig(self, soundId, em, loop, vol, pitch, randomStartPosition);

        foreach (var player in self.PlayersInRoom)
        {
            try
            {
                if (playerNoises.TryGetValue(player.playerState.playerNumber, out var noise))
                {
                    var mic = self.game.cameras[0].virtualMicrophone;
                    if (mic == null) continue;
                    var soundData = mic.GetSoundData(soundId, -1);
                    float intensity = vol * soundData.vol * mic.VolFromPoint(em.pos, 0, soundData.range) * Options.SoundFright.Value;
                    noise.queuedIntensity += intensity;
                    noise.queuedBias += intensity * (em.pos.x < player.mainBodyChunk.pos.x ? 1 : -1);
                }
            }
            catch { }//(Exception ex) { Logger.LogError(ex); }
        }
    }

    private void Player_checkInput(On.Player.orig_checkInput orig, Player self)
    {
        orig(self);

        try
        {

            //if (self.slugcatStats.name != WatcherEnums.SlugcatStatsName.Watcher) return;
            if (!Options.AllSlugcats.Value //if all slugcats = true, don't return; ... if can't find slugcat or config == false, return
                && !(Options.SlugcatsEnabled.TryGetValue(self.slugcatStats.name.value, out var config) && config.Value))
                return;

            //if (self.inShortcut) return; //don't process while in shortcuts; that's stupid

            //get fright intensity
            float rightBias = 0; //if >1, prefers to move right
            float intensity = 0;
            var threat = Custom.rainWorld?.processManager?.musicPlayer?.threatTracker;
            if (threat == null) return; //nothing to track!

            //easy threats
            float ghostThreat = threat.ghostMode * Options.GhostFright.Value;
            intensity += ghostThreat;

            var ghostRoom = self.abstractPhysicalObject.world.worldGhost?.ghostRoom;
            if (ghostRoom == self.room.abstractRoom)
            {
                var ghost = self.room.updateList.FirstOrDefault(obj => obj is Ghost);
                if (ghost != null) //right bias if ghost is to the left
                    rightBias += ghostThreat * ((ghost as Ghost).pos.x < self.mainBodyChunk.pos.x ? 1 : -1);
            }

            intensity += threat.currentMusicAgnosticThreat * Options.CreatureFright.Value;

            if (!playerNoises.TryGetValue(self.playerState.playerNumber, out var noise))
            {
                noise = new();
                playerNoises.Add(self.playerState.playerNumber, noise);
                Logger.LogDebug($"Added noise generator for player {self.playerState.playerNumber}");
            }

            bool canForceMovement = !self.inShortcut //duh
                && !self.input[0].mp //don't affect map
                && !self.input[0].spec //don't move while trying to hide
                && self.canJump > 0 //don't move mid-air
                && !(self.input[0].x == 0 && self.input[0].y != 0) //don't move while holding straight down or straight up
                && !self.input[0].thrw //don't alter throw trajectories
                && self.bodyMode != Player.BodyModeIndex.WallClimb; //don't ruin wall slides/jumps
                //&& !self.input[0].jmp; //this is a HUGE protection, but it allows players to easily circumvent the effect if they want

            //expensive frights
            bool fullCheck = noise.FullIntensityCheck();
            if (fullCheck)
            {
                //rot threat
                var critters = self.room.physicalObjects.Aggregate((l1, l2) => l1.Concat(l2).ToList());
                foreach (var obj in critters)
                {
                    if (obj is Creature cr)
                    {
                        float thr = threat.threatDetermine.ThreatOfCreature(cr, self);
                        float critFright = thr * 0.5f * Options.CreatureFright.Value; //set at a lower value; mostly just used for movement bias

                        //rot handling
                        if (cr is Lizard liz && liz.rotModule != null)
                            critFright += thr * Options.RotFright.Value * (liz.rotModule.RotSizeClass ? 2 : 1);
                        else if (cr is DaddyLongLegs)
                            critFright += thr * Options.RotFright.Value;

                        //detect if movement should be disabled due to the creature being an imminent threat
                        if (thr >= 0.4f && (self.mainBodyChunk.pos - cr.mainBodyChunk.pos).sqrMagnitude < 20000) //~7 tiles
                            canForceMovement = false;

                        intensity += critFright;
                        rightBias += 2 * critFright * (cr.mainBodyChunk.pos.x < self.mainBodyChunk.pos.x ? 1 : -1);
                    }
                    //iterators
                    else if (obj is Oracle oracle)
                    {
                        intensity += 2f * Options.WeirdnessFright.Value;
                        rightBias += 2f * Options.WeirdnessFright.Value * (oracle.firstChunk.pos.x < self.mainBodyChunk.pos.x ? 1 : -1);
                    }
                }

                //weird effect threat
                foreach (var rip in self.room.cosmeticRipples)
                {
                    //0 when outside the ripple range; 1 when at center, squared relationship
                    float sqrScale = rip.scale * rip.scale * 16; //* 16 = 4x range
                    float ripInt = Mathf.Max(0, -((self.mainBodyChunk.pos - rip.pos).sqrMagnitude - sqrScale) / sqrScale) * Options.WeirdnessFright.Value;
                    intensity += ripInt;
                    rightBias += ripInt * (rip.pos.x < self.mainBodyChunk.pos.x ? 1 : -1);
                }

                //intensity += self.room.warpPoints.Count * Options.WeirdnessFright.Value;
                foreach (var warp in self.room.warpPoints)
                {
                    //0 when outside the warp effect range; 1 when at center, squared relationship
                    float sqrScale = warp.radius * warp.radius * 100; //* 100 = 10x range
                    float warpInt = Mathf.Max(0, -((self.mainBodyChunk.pos - warp.pos).sqrMagnitude - sqrScale) / sqrScale) * Options.WeirdnessFright.Value;
                    intensity += warpInt;
                    rightBias += warpInt * (warp.pos.x < self.mainBodyChunk.pos.x ? 1 : -1);
                }
            }

            //easy rot effect
            if (self.room.rotPresenceInitialized) intensity += 0.5f * Options.RotFright.Value;

            if (ModManager.Watcher)
            {
                intensity += self.room.roomSettings.GetEffectAmount(WatcherEnums.RoomEffectType.SentientRotParticles)
                    * 0.5f * (Options.WeirdnessFright.Value + Options.RotFright.Value); //avg. of weird and rot
                intensity += self.room.roomSettings.GetEffectAmount(WatcherEnums.RoomEffectType.SentientRotInfection)
                    * Options.RotFright.Value;
            }

            //easy weirdness effects
            if (self.room.fsRipple != null) intensity += Options.WeirdnessFright.Value;

            if (self.room.lightningMaker != null) intensity += self.room.lightningMaker.blinded * Options.WeirdnessFright.Value;

            if (self.room.lightning != null) intensity += self.room.lightning.intensity * Options.WeirdnessFright.Value;

            if (self.room.ripple) intensity += Options.WeirdnessFright.Value;

            intensity += self.room.voidSpawns.Count * 0.05f * Options.WeirdnessFright.Value;


            //check other rooms
            foreach (var exit in self.room.exitAndDenIndex)
            {
                var abRoom = self.room.WhichRoomDoesThisExitLeadTo(exit);
                if (abRoom != null)
                {
                    Vector2 realPos = (exit * 20).ToVector2();
                    float roomInt = 2 * IntensityOfRoom(abRoom, fullCheck) * Mathf.Max(0, 1 - ((realPos-self.mainBodyChunk.pos) * 0.005f).sqrMagnitude); //10 tile range (1 / 20 / 10 == 0.005)

                    intensity += roomInt;
                    rightBias += roomInt * (self.mainBodyChunk.pos.x <= realPos.x ? -1 : 1);
                }
            }


            //slugpup AI stats
            if (self.isNPC)
            {
                var per = self.abstractCreature.personality;
                intensity *= 1 + 0.5f * (per.nervous - 0.5f*per.bravery);
                rightBias *= 1 + 0.5f * (per.nervous - per.bravery);
            }

            //finally get to the actual movement stuff
            noise.Tick(intensity, rightBias);
            if (noise.fright < 0.1f) return; //don't process really low intensities; that's a waste of processing

            float moveDir = noise.GetMoveDir();
            //Logger.LogDebug($"Int: {intensity}, bias: {rightBias}, accInt: {noise.accustomedIntensity}, immFright: {noise.immediateFright}, fright: {noise.fright}, moveDir: {moveDir}");

            if (self.bodyMode != Player.BodyModeIndex.Stand)
                moveDir *= 0.7f; //significantly reduce if crawling

            moveDir *= 1.1f - self.aerobicLevel * 0.2f; //slightly less movement if adrenaline is high

            //Only Cancel Inputs
            if (Options.OnlyWhileMoving.Value && self.input[0].x == 0)
                canForceMovement = false;

            float rand = UnityEngine.Random.value;
            if (canForceMovement)
            {
                if (moveDir >= 1) //move right
                {
                    if (Options.NoReversing.Value && self.input[0].x == -1)
                    {
                        self.input[0].x = 0; //no reversing! Just cancel movement
                    }
                    else
                    {
                        bool canMove = false;
                        for (int i = 0; i > -5; i--) //check to ensure I'm not running into the abyss
                        {
                            if (self.IsTileSolid(0, 1, i)) { canMove = true; break; }
                        }
                        if (canMove)
                            self.input[0].x = 1; //override input with moveDir
                    }
                }
                else if (moveDir <= -1) //move left
                {
                    if (Options.NoReversing.Value && self.input[0].x == 1)
                    {
                        self.input[0].x = 0; //no reversing! Just cancel movement
                    }
                    else
                    {
                        bool canMove = false;
                        for (int i = 0; i > -5; i--)
                        {
                            if (self.IsTileSolid(0, -1, i)) { canMove = true; break; }
                        }
                        if (canMove)
                            self.input[0].x = -1;
                    }
                }

                if (Mathf.Abs(moveDir) >= 1.3f && rand < 0.01f)
                    self.input[0].y = -1; //chance to start crawling
            }

            
            //graphical effects

            //panic breathing when recovering from a fright
            if (noise.causedExhaustion) //fix the extra slowdown from lungsExhausted
            {
                self.slowMovementStun = 1; //barely any slowdown
                noise.causedExhaustion = false;
                self.lungsExhausted = false;
            }

            //self.aerobicLevel = Mathf.Max(self.aerobicLevel, noise.fright);
            if (!self.submerged && noise.immediateFright < 0 && noise.fright > 0.3f)
            {
                self.airInLungs = Mathf.Min(self.airInLungs, Mathf.Max(0.7f, 1f + noise.immediateFright * 0.15f));
                if (self.airInLungs < 0.9f)
                {
                    self.lungsExhausted = true;
                    noise.causedExhaustion = true;
                }
            }
            if (self.graphicsModule != null)
            {
                (self.graphicsModule as PlayerGraphics).breath = Mathf.Clamp(1f - noise.fright, 0, (self.graphicsModule as PlayerGraphics).breath);

                //blink
                if (rand < 0.04f && rand < noise.fright) (self.graphicsModule as PlayerGraphics).blink = 2 + (int)(rand * 2f);
            }

            //shake
            Vector2 nudge = new(moveDir * 0.5f + (rand * 2 - 1) * Mathf.Min(noise.fright, 2f), 0);
            (self.graphicsModule as PlayerGraphics).NudgeDrawPosition(0, nudge);
            (self.graphicsModule as PlayerGraphics).NudgeDrawPosition(1, nudge);

        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
        }
    }

    private float IntensityOfRoom(AbstractRoom abRoom, bool fullTest)
    {
        float intensity = 0f;

        //echoes
        var ghostRoom = abRoom.world.worldGhost?.ghostRoom;
        if (abRoom == ghostRoom) intensity += Options.GhostFright.Value;

        //iterators
        if (abRoom.name.EndsWith("_AI"))
            intensity += 2f * Options.WeirdnessFright.Value;

        //rot + creatures
        if (fullTest)
        {
            foreach (var crit in abRoom.creatures)
            {
                intensity += 0.5f * crit.creatureTemplate.dangerousToPlayer * Options.CreatureFright.Value;
                if (crit.state is LizardState liz)
                {
                    if (liz.rotType == LizardState.RotType.Full) intensity += 2f * Options.RotFright.Value;
                    else if (liz.rotType == LizardState.RotType.Slight || liz.rotType == LizardState.RotType.Opossum) intensity += Options.RotFright.Value;
                }
                else if (crit.creatureTemplate.type == CreatureTemplate.Type.DaddyLongLegs)
                    intensity += Options.RotFright.Value;
            }
        }

        if (abRoom.realizedRoom != null)
        {
            var room = abRoom.realizedRoom;

            if (room.rotPresenceInitialized) intensity += Options.RotFright.Value;

            //easy weirdness effects
            if (room.fsRipple != null) intensity += Options.WeirdnessFright.Value;

            if (room.lightningMaker != null) intensity += room.lightningMaker.blinded * Options.WeirdnessFright.Value;

            if (room.lightning != null) intensity += room.lightning.intensity * Options.WeirdnessFright.Value;

            if (room.ripple) intensity += Options.WeirdnessFright.Value;

            intensity += room.voidSpawns.Count * 0.05f * Options.WeirdnessFright.Value;
        }

        return intensity;
    }

    private static Dictionary<int, MovementNoise> playerNoises = new();

    private class MovementNoise
    {
        private int _updateCounter = 0;
        public bool FullIntensityCheck()
        {
            return true; //for now, screw processing!!
            if (_updateCounter++ >= 4)
            {
                _updateCounter = 0;
                return true;
            }
            return false;
        }

        public bool causedExhaustion = false;

        //used for sounds
        public float queuedIntensity = 0, queuedBias = 0;

        public float fright = 0;
        public float rightBias = 0;
        public float immediateFright = 0;
        public float accustomedIntensity = 0;

        public const float OVERALL_MODIFIER = 1f;

        public MovementNoise()
        {
            //...do I need anything here?
        }

        public void Tick(float intensity, float bias)
        {
            intensity += queuedIntensity; bias += queuedBias;
            queuedIntensity = 0; queuedBias = 0;
            intensity *= OVERALL_MODIFIER; bias *= OVERALL_MODIFIER;

            accustomedIntensity += (intensity - accustomedIntensity) * 0.003f; //move .3% towards new intensity
            accustomedIntensity *= 0.999f; //always decreasing; never truly gets used to frights

            immediateFright = intensity - accustomedIntensity;
            if (immediateFright > fright) fright = Mathf.Min(fright + (immediateFright - fright) * 1.2f, Options.MaxIntensity.Value); //* 1.2f = jumpscare effect
            else fright += (Mathf.Max(0, immediateFright) - fright) * 0.01f; //move 1% towards new fright

            if (intensity == 0)
                rightBias += (0 - rightBias) * 0.05f; //move 5% towards 0
            else
                rightBias += (bias / intensity - rightBias) * 0.05f; //move 5% towards new movement bias
            rightBias = Mathf.Clamp(rightBias, Options.MaxIntensity.Value * (-0.5f), Options.MaxIntensity.Value * 0.5f);
        }

        public float GetMoveDir()
        {
            //float rand = UnityEngine.Random.value;
            float n = noise(UnityEngine.Random.value);
            n += rightBias;
            //n *= fright * (1 + rand * rand * rand); //weird spikes occassionally occur

            return n == float.NaN ? 0 : n * fright; //don't use NaN
        }

        private float t = 1, a = 0, b = 0;
        private float noise(float step)
        {
            t += step * 0.3f;
            while (t >= 1)
            {
                a = b;
                float newRand = UnityEngine.Random.value * 2 - 1;
                b = 0.5f * (newRand + newRand*newRand*newRand); //change of big upward spike
                t--;
            }
            return lerp();
        }
        private float lerp() => a + fade() * (b - a);
        private float fade() => t * t * t * (10 + t * (-15 + 6 * t));
    }
}
