using UnityEngine;

namespace NhxMagicWeapons.Armor
{
    // A set's bonus: the game's stats (the Firecaller's Regalia's eitr regen), and one element's damage up by m_perLevel for
    // each level of the set's least upgraded piece worn: fire for the Firecaller's Regalia (the user, 2026-11-10: "3 pieces
    // upgraded to level 5 and one level 3 will increase fire damage by 3%, while all level 5 = 5%"), lightning for
    // Stormcaller's Raiment ("we can also add the +% lightning damage just like the fire set did"). The game applies it to
    // every attack's damage of that element (SE_Stats.ModifyAttack, through m_percentigeDamageModifiers, whatever the
    // skill), and shows it in the effect's tooltip ("Fire: +3%"). The staves look for the sets' effects by their names
    // (StormcallerArmor.FireSetBonus, StormSetBonus).
    // And with the set's staff in hand, each hit leaves what it hits one more step weaker to the element (the user,
    // 2026-11-11: "with the armor set bonus, lower resistance by one step per hit instead"; Staves\ElementWeakness), the
    // staff alone none (2026-11-14: "it should only be active when paired with the staff's respective armor set"): its
    // attacks' HitData carry the set's weakness (SEMan.ModifyAttack, which every attack's HitData goes through, Lightning
    // Step's and the comet's too), and every hit made from them keeps it (chain jumps, blasts, tendrils, the storm, flames)
    internal class LevelSetBonus : SE_Stats
    {
        // The pieces counted, the element, and its damage per level of the least upgraded
        public string m_setName = "";
        public HitData.DamageType m_element = HitData.DamageType.Fire;
        public float m_perLevel = 0.01f;
        // The staff (its item's m_shared.m_name) and the weakness its hits bring (its tooltip is the set's line)
        public string m_staff = "";
        public StatusEffect m_weakness;

        // The levels don't change while it's on (a piece is upgraded unequipped, which ends it), but in case
        private const float CheckInterval = 1f;
        private float m_checkTimer;

        public override void Setup(Character character)
        {
            base.Setup(character);
            UpdateBonus();
        }

        public override void UpdateStatusEffect(float dt)
        {
            base.UpdateStatusEffect(dt);
            m_checkTimer -= dt;
            if (m_checkTimer <= 0f)
            {
                m_checkTimer = CheckInterval;
                UpdateBonus();
            }
        }

        private void UpdateBonus()
        {
            float bonus = m_perLevel * LowestLevel(m_character as Humanoid, m_setName);
            switch (m_element)
            {
                case HitData.DamageType.Fire:
                    m_percentigeDamageModifiers.m_fire = bonus;
                    break;
                case HitData.DamageType.Frost:
                    m_percentigeDamageModifiers.m_frost = bonus;
                    break;
                case HitData.DamageType.Lightning:
                    m_percentigeDamageModifiers.m_lightning = bonus;
                    break;
                case HitData.DamageType.Poison:
                    m_percentigeDamageModifiers.m_poison = bonus;
                    break;
                case HitData.DamageType.Spirit:
                    m_percentigeDamageModifiers.m_spirit = bonus;
                    break;
            }
        }

        public override void ModifyAttack(Skills.SkillType skill, ref HitData hitData)
        {
            base.ModifyAttack(skill, ref hitData);
            if (m_weakness && m_character is Humanoid humanoid)
            {
                ItemDrop.ItemData weapon = humanoid.GetCurrentWeapon();
                if (weapon != null && weapon.m_shared.m_name == m_staff)
                {
                    hitData.m_statusEffectHash = m_weakness.name.GetStableHashCode();
                }
            }
        }

        // The lowest level among the set's pieces worn (its four, while the effect is on; ArmorLegend too). On the wearer's
        // own game: other games don't have its items
        internal static int LowestLevel(Humanoid humanoid, string setName)
        {
            if (!humanoid)
            {
                return 0;
            }
            int lowest = int.MaxValue;
            foreach (ItemDrop.ItemData item in new[] { humanoid.m_helmetItem, humanoid.m_chestItem, humanoid.m_legItem, humanoid.m_shoulderItem })
            {
                if (item != null && item.m_shared.m_setName == setName)
                {
                    lowest = Mathf.Min(lowest, item.m_quality);
                }
            }
            return lowest == int.MaxValue ? 0 : lowest;
        }

        // On someone, the game's lines show the bonus as it is; in a piece's tooltip, where no one wears it, how it goes
        public override string GetTooltipString()
        {
            string text = base.GetTooltipString();
            if (!m_character)
            {
                text += $"$inventory_{m_element.ToString().ToLowerInvariant()}: <color=orange>+{m_perLevel * 100f:0.#}% $se_nhx_perlevel</color>\n";
            }
            if (m_weakness)
            {
                text += m_weakness.GetTooltipString() + "\n";
            }
            return text;
        }
    }
}
