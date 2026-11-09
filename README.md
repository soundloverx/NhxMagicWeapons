# NhxMagicWeapons

A private mod for our server: magic weapons made with Magic Supremacy's models and effects (https://valheim.hexium.gg/mods/Dreanegade/Magic_Supremacy). Not published.

Both staves are made at the Galdr Table, use Elemental Magic, and can be refined at the Forge of Potential with black marble battle idols, like the game's Mistlands staves. They're meant to go up to level 20: their power, their look and their effects grow with each level. Their durability is 400, and 100 more each level.

Their main attacks hit where your crosshair is, at once: no projectile to dodge or lead. Their attacks go through your friends and tames and don't hurt you (unless you've turned PvP on), and they damage buildings, as the game's staves do. Their eitr costs go down by up to a third with your Elemental Magic skill.

Damage, the same for both staves in all: 155 at level 1, 190 at 5, 243 at 10, 317 at 15, 392 at 20. From level 5 it adds spirit damage, which the Ashlands' Charred are weak to, and from level 10 more fire, which most of the Deep North's creatures are weak to.

## Staff of Storms

A lightning staff, made from 20 Yggdrasil wood, 16 refined eitr and 2 thunder stones (Haldor sells them); each level up to 4 takes 10 more Yggdrasil wood, 8 eitr and 1 thunder stone.

- **Attack: lightning bolt.** A bolt leaps from the staff's head to the first thing where you aim, up to 50 m, for 30 eitr. Lightning, plus spirit from level 5 and fire from level 10.
- **Chain lightning:** a 5% chance per level (100% at level 20) that the bolt jumps on to nearby enemies, up to 8 m from one to the next: 1 jump at levels 1-2, one more every 2 levels, up to 10. The first jump deals half the bolt's damage, each next one 15% less. It prefers enemies it hasn't hit yet.
- **Secondary attack (middle mouse), from level 5: call lightning.** Lightning strikes from the sky on the spot you aim at, up to 60 m (on the ground under it, if you aim at a creature or the sky): 2.2 times the bolt's damage to everything within 3.5 m, for 50 eitr.

The staff is Magic Supremacy's Thundersurge; its strike is the Obliterator's lightning.

## Wrath of Muspelheim

A fire staff, made from 20 Yggdrasil wood, 16 refined eitr and 4 surtling cores; each level up to 4 takes 10 more Yggdrasil wood, 8 eitr and 2 surtling cores. Held forward like the Incinerator's flamethrower.

- **Attack: fire beam.** A beam to the first thing where you aim, up to 50 m, for 30 eitr: one as you click, then one a second while you hold the button. Pierce and fire (two fifths pierce, as fire burns over time), plus spirit from level 5. Where it hits, it blasts everything else around for half its damage: 1 m around at level 1, 0.15 m more each level (2.35 m at 10, 3.85 m at 20).
- **Secondary attack (middle mouse, held), from level 5: flamethrower.** A cone of flames burning everything in front of you for 15% of the beam's damage five times a second, at 4 eitr each (20 a second). It reaches 7 m at level 5, a meter more every 3 levels, 12 m at 20.

The staff is Magic Supremacy's Incinerator, with its explosion and flames.

## Installing

Everyone on the server needs it, with the same version: the server, and every player. Copy the NhxMagicWeapons folder into BepInEx\plugins. It needs Jotunn.

It can't run with Magic Supremacy: it uses Magic Supremacy's asset bundle, which Unity can't load twice. With both installed, BepInEx doesn't load this mod.
