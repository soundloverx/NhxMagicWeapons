using NhxMagicWeapons.Staves.WrathOfMuspelheim.Effects;
using System.Collections.Generic;
using UnityEngine;

namespace NhxMagicWeapons.Staves.WrathOfMuspelheim
{
    // The Wrath of Muspelheim's beam with the Firecaller's Regalia (its set bonus, the user's design, 2026-11-10): one
    // beam, channelled for as long as the attack is held and there's eitr, "as if there's a spider thread connecting the
    // tip of the staff and the enemy, constantly undulating and pulsating".
    // - It snaps to the first enemy it touches and follows it, until the enemy dies, goes out of range, or stays out of
    //   sight for m_sightGrace seconds; then it follows the crosshair again, to the next enemy it touches. Until then it
    //   burns what it touches, as the beam does.
    // - Every second (the attack's shots, FireBeam, each paying its eitr: the game stops eitr regen during attacks), the
    //   enemy takes the beam's hit and explodes (its blast). From it, tendrils of fire shoot to every other enemy within
    //   m_tendrilRange in sight of it, however many, each taking the beam's full hit too and exploding; they don't send
    //   tendrils of their own. Each enemy takes one hit a tick (the user's choice): the blast spares those the tendrils hit.
    //   Without an enemy, what it touches takes the beam's hit and the blast, as from the beam.
    // The caster's game creates it at the first shot (Tick, from FireBeam.Setup), aims it every frame and deals the hits;
    // its ZDO tells every game (that one too) whom or where the thread goes, and each tick's blast and tendrils, which
    // every game draws. The caster's game destroys it when the attack ends.
    internal class FireChannel : MonoBehaviour
    {
        // The staff's beam (its prefab): its range, look, blast, sound
        public FireBeam m_beam;
        public float m_tendrilRange = 20f;
        // The tendrils shoot this long after the blast, and show this long
        public float m_tendrilDelay = 0.15f;
        public float m_tendrilDuration = 0.75f;
        // Explosions shown for the tendrils, at most: each is fire, smoke, light and sound
        public int m_maxTendrilBlasts = 12;
        public float m_sightGrace = 0.5f;
        // The attack shoots every second: without a shot for this long, the channel is over
        public float m_tickTimeout = 1.6f;
        // How fast a hit's flare along the thread dies down
        public float m_flareTime = 0.15f;

        private static readonly KeyValuePair<int, int> s_casterKey = ZDO.GetHashZDOID("nhx_channel_caster");
        private static readonly KeyValuePair<int, int> s_targetKey = ZDO.GetHashZDOID("nhx_channel_target");
        private static readonly int s_levelKey = "nhx_channel_level".GetStableHashCode();
        private static readonly int s_endKey = "nhx_channel_end".GetStableHashCode();
        private static readonly int s_tickKey = "nhx_channel_tick".GetStableHashCode();
        private static readonly int s_blastKey = "nhx_channel_blast".GetStableHashCode();
        private static readonly int s_blastedKey = "nhx_channel_blasted".GetStableHashCode();
        private static readonly int s_tendrilsKey = "nhx_channel_tendrils".GetStableHashCode();
        private const float EndMoved = 0.1f;

        // On the caster's game: each caster's channel
        private static readonly Dictionary<Character, FireChannel> s_channels = new Dictionary<Character, FireChannel>();
        private static readonly List<Character> s_nearby = new List<Character>();

        private ZNetView m_nview;

        // On every game: the thread, from when the ZDO has its caster
        private FireBeams m_thread;
        private BeamSize m_tendrilSize;
        private Character m_caster;
        private Transform m_tip;
        private int m_level;
        private float m_age;
        private float m_flareAge = float.MaxValue;
        private int m_seenTick;
        // Its sound (not on a dedicated server), which ends by itself once this is gone
        private ChannelSound m_sound;
        private ZDOID m_shownTargetId = ZDOID.None;
        private Character m_shownTarget;
        private Vector3 m_shownEnd;
        private readonly List<Tendril> m_tendrils = new List<Tendril>();

        // On the caster's game
        private bool m_isCaster;
        private Character m_owner;
        private Humanoid m_humanoid;
        private Attack m_attack;
        private Character m_target;
        private Vector3 m_end;
        private Vector3 m_writtenEnd = new Vector3(float.MaxValue, 0f, 0f);
        private Collider m_endCollider;
        private float m_unseen;
        private float m_lastTick;
        private int m_tick;
        private readonly List<Character> m_tendrilTargets = new List<Character>();
        private HitData m_tendrilHit;
        private Vector3 m_tendrilFrom;
        private float m_tendrilTime = -1f;

        private class Tendril
        {
            public GameObject m_root;
            public FireBeams m_beams;
            public Character m_from;
            public Vector3 m_fromPoint;
            public Character m_to;
            public Vector3 m_toPoint;
            public float m_start;
            public bool m_blast;
            public bool m_shown;
        }

        private void Awake()
        {
            m_nview = GetComponent<ZNetView>();
        }

        // One of the attack's shots, on the caster's game: the caster's channel, begun with the first
        internal static void Tick(GameObject prefab, Character owner, HitData hitData, int level, float hitNoise)
        {
            s_channels.TryGetValue(owner, out FireChannel channel);
            // A new attack begins a new channel, even when the last one hasn't seen its attack end yet
            Humanoid humanoid = owner as Humanoid;
            if (channel && humanoid && channel.m_attack != humanoid.m_currentAttack)
            {
                channel.End();
                channel = null;
            }
            if (!channel)
            {
                Transform tip = StaffHits.FindTip(owner);
                channel = Instantiate(prefab, tip ? tip.position : owner.GetCenterPoint(), Quaternion.identity).GetComponent<FireChannel>();
                channel.Begin(owner, level);
                s_channels[owner] = channel;
            }
            channel.Hit(hitData, level, hitNoise);
        }

        private void Begin(Character owner, int level)
        {
            m_isCaster = true;
            m_owner = owner;
            m_humanoid = owner as Humanoid;
            // The attack shooting: the channel lasts as long as it does
            m_attack = m_humanoid ? m_humanoid.m_currentAttack : null;
            ZDO zdo = m_nview.GetZDO();
            zdo.Set(s_casterKey, owner.GetZDOID());
            zdo.Set(s_levelKey, level);
            Aim();
            WriteAim(zdo);
            // The beam's shot as it begins (networked: every game hears it), the channel's sound swelling after it (the
            // user: "keep the original laser sound when it begins and then continue with the looping sound [...] tapping
            // for single shots doesn't feel as impactful anymore")
            StaffEffects.Create(m_beam.m_castEffects, transform.position, Quaternion.LookRotation(owner.GetLookDir()));
        }

        // The tick's hits
        private void Hit(HitData hitData, int level, float hitNoise)
        {
            m_lastTick = Time.time;
            Aim();
            Vector3 start = Start();
            float radius = m_beam.BlastRadius(level);
            HashSet<GameObject> done = new HashSet<GameObject>();
            bool hitCharacter = false;
            bool blasted = false;
            Vector3 blastPoint = m_end;
            m_tendrilTargets.Clear();
            m_tendrilTime = -1f;

            if (m_target)
            {
                Vector3 center = m_target.GetCenterPoint();
                Vector3 dir = Direction(start, center, m_owner.transform.forward);
                StaffHits.Damage(m_owner, hitData, m_target.GetCollider(), center, dir, out _);
                hitCharacter = true;
                done.Add(m_target.gameObject);
                FindTendrils(center);
                foreach (Character tendril in m_tendrilTargets)
                {
                    done.Add(tendril.gameObject);
                }
                hitCharacter |= m_beam.Blast(m_owner, hitData, center, dir, radius, done);
                blasted = true;
                blastPoint = center;
                if (m_tendrilTargets.Count > 0)
                {
                    m_tendrilHit = hitData.Clone();
                    m_tendrilFrom = center;
                    m_tendrilTime = Time.time + m_tendrilDelay;
                }
            }
            else if (m_endCollider)
            {
                Vector3 dir = Direction(start, m_end, m_owner.transform.forward);
                hitCharacter = StaffHits.Damage(m_owner, hitData, m_endCollider, m_end, dir, out Character struck) && struck;
                done.Add(Projectile.FindHitObject(m_endCollider));
                hitCharacter |= m_beam.Blast(m_owner, hitData, m_end, dir, radius, done);
                blasted = true;
            }
            if (hitCharacter)
            {
                StaffHits.OnCharacterHit(m_owner, hitData);
            }
            if (blasted)
            {
                BaseAI.DoProjectileHitNoise(blastPoint, hitNoise, m_owner);
            }

            ZDO zdo = m_nview.GetZDO();
            WriteAim(zdo);
            zdo.Set(s_blastKey, blastPoint);
            zdo.Set(s_blastedKey, blasted);
            zdo.Set(s_tendrilsKey, m_tendrilTargets.Count);
            for (int i = 0; i < m_tendrilTargets.Count; i++)
            {
                zdo.Set(TendrilKey(i), m_tendrilTargets[i].GetZDOID());
                zdo.Set(TendrilPointKey(i), m_tendrilTargets[i].GetCenterPoint());
            }
            zdo.Set(s_tickKey, ++m_tick);
        }

        private static KeyValuePair<int, int> TendrilKey(int i)
        {
            return ZDO.GetHashZDOID("nhx_channel_tendril" + i);
        }

        private static int TendrilPointKey(int i)
        {
            return ("nhx_channel_tendril_point" + i).GetStableHashCode();
        }

        // Every other enemy within m_tendrilRange of the one the thread holds, alive and in sight of it
        private void FindTendrils(Vector3 center)
        {
            s_nearby.Clear();
            Character.GetCharactersInRange(center, m_tendrilRange, s_nearby);
            foreach (Character character in s_nearby)
            {
                if (character == m_target || character == m_owner || character.IsDead())
                {
                    continue;
                }
                if (StaffHits.IsValidTarget(m_owner, character) && StaffHits.InSight(center, character.GetCenterPoint()))
                {
                    m_tendrilTargets.Add(character);
                }
            }
            s_nearby.Clear();
        }

        // The tendrils' hits, as they reach their enemies
        private void HitTendrils()
        {
            if (m_tendrilTime < 0f || Time.time < m_tendrilTime)
            {
                return;
            }
            m_tendrilTime = -1f;
            foreach (Character target in m_tendrilTargets)
            {
                if (!target || target.IsDead())
                {
                    continue;
                }
                Vector3 point = target.GetCenterPoint();
                StaffHits.Damage(m_owner, m_tendrilHit, target.GetCollider(), point, Direction(m_tendrilFrom, point, target.transform.forward), out _);
            }
            m_tendrilTargets.Clear();
        }

        // Whom or where the thread goes: the enemy it holds while it can, else what's under the crosshair, holding it if
        // it's an enemy
        private void Aim()
        {
            Vector3 start = Start();
            if (m_target && Keep(m_target, start))
            {
                m_end = m_target.GetCenterPoint();
                m_endCollider = null;
                return;
            }
            m_target = null;
            float range = m_beam.m_range;
            Vector3 dir = StaffHits.AimAtCrosshair(m_owner, start, m_owner.GetLookDir(), range);
            Vector3 from = m_owner.GetCenterPoint();
            from.y = start.y;
            if (StaffHits.Trace(m_owner, from, start, start + dir * range, out Vector3 point, out Collider collider))
            {
                GameObject hit = Projectile.FindHitObject(collider);
                Character character = hit ? hit.GetComponent<Character>() : null;
                if (character && !character.IsDead())
                {
                    m_target = character;
                    m_unseen = 0f;
                    m_end = character.GetCenterPoint();
                    m_endCollider = null;
                    return;
                }
                m_end = point;
                m_endCollider = collider;
            }
            else
            {
                m_end = start + dir * range;
                m_endCollider = null;
            }
        }

        // Whether the thread keeps its enemy: alive, within the beam's range, and not out of sight for too long
        private bool Keep(Character target, Vector3 start)
        {
            if (target.IsDead())
            {
                return false;
            }
            Vector3 center = target.GetCenterPoint();
            if (Vector3.Distance(m_owner.GetCenterPoint(), center) > m_beam.m_range)
            {
                return false;
            }
            m_unseen = StaffHits.InSight(start, center) ? 0f : m_unseen + Time.deltaTime;
            return m_unseen < m_sightGrace;
        }

        private void WriteAim(ZDO zdo)
        {
            zdo.Set(s_targetKey, m_target ? m_target.GetZDOID() : ZDOID.None);
            // Where it ends, for the games where its enemy isn't (any more): only when it moved, as it's sent each time
            if ((m_end - m_writtenEnd).sqrMagnitude > EndMoved * EndMoved)
            {
                zdo.Set(s_endKey, m_end);
                m_writtenEnd = m_end;
            }
        }

        private Vector3 Start()
        {
            Transform tip = StaffHits.FindTip(m_owner);
            return tip ? tip.position : m_owner.GetCenterPoint();
        }

        private static Vector3 Direction(Vector3 from, Vector3 to, Vector3 otherwise)
        {
            Vector3 dir = to - from;
            return dir.sqrMagnitude > 0.001f ? dir.normalized : otherwise;
        }

        // Whether the attack that began the channel still goes on
        private bool Attacking()
        {
            return m_humanoid && m_attack != null && m_humanoid.m_currentAttack == m_attack && !m_attack.IsDone();
        }

        private void Update()
        {
            if (!m_nview.IsValid())
            {
                return;
            }
            if (m_isCaster)
            {
                if (!m_owner || m_owner.IsDead() || !Attacking() || Time.time - m_lastTick > m_tickTimeout)
                {
                    End();
                    return;
                }
                Aim();
                WriteAim(m_nview.GetZDO());
                HitTendrils();
            }
            else if (m_nview.IsOwner())
            {
                // Its caster left the game, and this one took it over
                ZNetScene.instance.Destroy(gameObject);
                return;
            }
            Draw(Time.deltaTime);
        }

        private void End()
        {
            if (m_owner && s_channels.TryGetValue(m_owner, out FireChannel channel) && channel == this)
            {
                s_channels.Remove(m_owner);
            }
            m_isCaster = false;
            ZNetScene.instance.Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (m_isCaster && m_owner && s_channels.TryGetValue(m_owner, out FireChannel channel) && channel == this)
            {
                s_channels.Remove(m_owner);
            }
        }

        // On every game
        private void Draw(float dt)
        {
            if (m_thread == null && !Init())
            {
                return;
            }
            m_age += dt;
            m_flareAge += dt;
            ZDO zdo = m_nview.GetZDO();

            ZDOID targetId = zdo.GetZDOID(s_targetKey);
            if (targetId != m_shownTargetId)
            {
                m_shownTargetId = targetId;
                GameObject target = targetId.IsNone() ? null : ZNetScene.instance.FindInstance(targetId);
                m_shownTarget = target ? target.GetComponent<Character>() : null;
            }
            if (m_shownTarget)
            {
                m_shownEnd = m_shownTarget.GetCenterPoint();
            }
            else if (zdo.GetVec3(s_endKey, out Vector3 end))
            {
                m_shownEnd = end;
            }

            if (!m_tip && m_caster)
            {
                m_tip = StaffHits.FindTip(m_caster);
            }
            Vector3 start = m_tip ? m_tip.position : transform.position;
            // Along the staff, out of its head
            Vector3 bend = m_tip ? m_tip.forward : Vector3.zero;

            int tick = zdo.GetInt(s_tickKey);
            if (tick != m_seenTick)
            {
                m_seenTick = tick;
                OnTick(zdo);
            }
            float flare = m_flareAge < 10f * m_flareTime ? Mathf.Exp(-m_flareAge / m_flareTime) : 0f;
            m_thread.DrawThread(m_age, start, bend, m_shownEnd, flare);
            if (m_sound)
            {
                m_sound.Follow(start, m_age, m_flareAge);
            }
            DrawTendrils();
        }

        private bool Init()
        {
            ZDO zdo = m_nview.GetZDO();
            ZDOID casterId = zdo.GetZDOID(s_casterKey);
            if (casterId.IsNone())
            {
                return false;
            }
            GameObject caster = ZNetScene.instance.FindInstance(casterId);
            m_caster = caster ? caster.GetComponent<Character>() : null;
            m_level = zdo.GetInt(s_levelKey, 1);
            BeamSize size = m_beam.m_look.Size(m_level, m_beam.m_levelsPerStrand, m_beam.m_maxStrands);
            m_thread = new FireBeams(transform, m_beam.m_look, 1f, null, transform.position, transform.position, size);
            // The tendrils as many strands as the thread, and as wide
            m_tendrilSize = size;
            m_shownEnd = transform.position;
            if (ZNet.instance && !ZNet.instance.IsDedicated())
            {
                m_sound = ChannelSound.Create(this, SoundTemplate());
            }
            return true;
        }

        // The beam's shot's sound: 3D, its distances, and the game's mixer group (AudioRouting), for the channel's
        private AudioSource SoundTemplate()
        {
            foreach (GameObject effect in m_beam.m_castEffects)
            {
                AudioSource source = effect ? effect.GetComponentInChildren<AudioSource>(true) : null;
                if (source)
                {
                    return source;
                }
            }
            return null;
        }

        // A tick, on this game: the thread flares, its enemy explodes, and the tendrils shoot from it
        private void OnTick(ZDO zdo)
        {
            m_flareAge = 0f;
            if (!zdo.GetBool(s_blastedKey) || !zdo.GetVec3(s_blastKey, out Vector3 blast))
            {
                return;
            }
            m_beam.ShowBlast(blast, m_level, 1f);

            int count = zdo.GetInt(s_tendrilsKey);
            for (int i = 0; i < count; i++)
            {
                zdo.GetVec3(TendrilPointKey(i), out Vector3 point);
                ZDOID id = zdo.GetZDOID(TendrilKey(i));
                GameObject target = id.IsNone() ? null : ZNetScene.instance.FindInstance(id);
                GameObject root = new GameObject("tendril");
                root.transform.SetParent(transform, false);
                m_tendrils.Add(new Tendril
                {
                    m_root = root,
                    m_beams = new FireBeams(root.transform, m_beam.m_look, m_tendrilDuration, null, blast, point, m_tendrilSize, false),
                    m_from = m_shownTarget,
                    m_fromPoint = blast,
                    m_to = target ? target.GetComponent<Character>() : null,
                    m_toPoint = point,
                    m_start = m_age + m_tendrilDelay,
                    m_blast = i < m_maxTendrilBlasts,
                });
            }
        }

        // From the enemy the thread holds to each other one, following them, with the thread's look; each explodes as its
        // tendril reaches it (no camera shake: there may be many)
        private void DrawTendrils()
        {
            for (int i = m_tendrils.Count - 1; i >= 0; i--)
            {
                Tendril tendril = m_tendrils[i];
                float age = m_age - tendril.m_start;
                if (age >= m_tendrilDuration)
                {
                    Destroy(tendril.m_root);
                    m_tendrils.RemoveAt(i);
                    continue;
                }
                if (tendril.m_from)
                {
                    tendril.m_fromPoint = tendril.m_from.GetCenterPoint();
                }
                if (tendril.m_to)
                {
                    tendril.m_toPoint = tendril.m_to.GetCenterPoint();
                }
                if (age >= 0f && !tendril.m_shown)
                {
                    tendril.m_shown = true;
                    if (tendril.m_blast)
                    {
                        m_beam.ShowBlast(tendril.m_toPoint, m_level, 0f);
                    }
                }
                tendril.m_beams.DrawTendril(age, tendril.m_fromPoint, tendril.m_toPoint);
            }
        }
    }
}
