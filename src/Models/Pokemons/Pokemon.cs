using System.Collections.Generic;

namespace Pokebot.Models.Pokemons
{
    public class Pokemon
    {
        public uint PID { get; }
        public PokemonOriginalTrainer? OriginalTrainer { get; }
        public string Nickname { get; }
        public PokemonLanguageCode Language { get; }
        public PokemonEggNameType EggName { get; }
        public PokemonMark Markings { get; }
        public int Checksum { get; }
        public int CalculatedChecksum { get; }

        //Growth
        public int Species { get; }
        public PokemonItem? HeldItem { get; }
        public uint Experience { get; }
        public int PPBonuses { get; }
        public int Friendship { get; }

        //Attacks
        public List<PokemonAttack> Moves { get; }

        //EVs & Condition
        public PokemonEVS EVs { get; }
        public PokemonCondition? Conditions { get; }

        //Miscellaneous
        public PokemonPokerus? Pokerus { get; }
        public PokemonLocation? MetLocation { get; }
        public int MetLevel { get; }
        public PokemonOriginInfo? Origin { get; }
        public PokemonIVS IVs { get; }
        public bool IsEgg { get; }
        public PokemonRibbon? Ribbons { get; }

        //Others ?
        public string RealName { get; }
        public int DexId { get; }
        public bool IsShiny { get; }
        public PokemonNature? Nature { get; }
        public List<PokemonType> Types { get; }
        public string? Ability { get; }
        public PokemonType? HiddenPower { get; }
        public int HiddenPowerDamage { get; }
        public PokemonGender Gender { get; }
        public MemoryLocation MemoryLocation { get; }

        public Pokemon(uint pid, PokemonOriginalTrainer? originalTrainer, string nickname, PokemonLanguageCode language, PokemonEggNameType eggName, PokemonMark markings, int checksum, int calculatedChecksum, int species, PokemonItem? heldItem, uint experience, int pPBonuses, int friendship, List<PokemonAttack> moves, PokemonEVS eVs, PokemonCondition? conditions, PokemonPokerus? pokerus, PokemonLocation? metLocation, int metLevel, PokemonOriginInfo? origin, PokemonIVS iVs, bool isEgg, PokemonRibbon? ribbons, string realName, int dexId, bool isShiny, PokemonNature? nature, List<PokemonType> types, string? ability, PokemonType? hiddenPower, int hiddenPowerDamage, PokemonGender gender, MemoryLocation memoryLocation)
        {
            PID = pid;
            OriginalTrainer = originalTrainer;
            Nickname = nickname;
            Language = language;
            EggName = eggName;
            Markings = markings;
            Checksum = checksum;
            CalculatedChecksum = calculatedChecksum;
            Species = species;
            HeldItem = heldItem;
            Experience = experience;
            PPBonuses = pPBonuses;
            Friendship = friendship;
            Moves = moves;
            EVs = eVs;
            Conditions = conditions;
            Pokerus = pokerus;
            MetLocation = metLocation;
            MetLevel = metLevel;
            Origin = origin;
            IVs = iVs;
            IsEgg = isEgg;
            Ribbons = ribbons;
            RealName = realName;
            DexId = dexId;
            IsShiny = isShiny;
            Nature = nature;
            Types = types;
            Ability = ability;
            HiddenPower = hiddenPower;
            HiddenPowerDamage = hiddenPowerDamage;
            Gender = gender;
            MemoryLocation = memoryLocation;
        }
        /// <summary>IV order used by <see cref="GetLogSummary"/>.</summary>
        public const string LogIVsOrder = "HP/Atk/Def/SpA/SpD/Spe";

        /// <summary>
        /// One line with the main info, written in the Logs tab for every encounter / reset. The most useful parts
        /// come first (the tab cuts long lines), e.g.
        /// "Uxie Lv.50 | Not shiny | IVs 31/31/27/15/29/8 = 141 | Nature: Impish | Ability: Levitate | Genderless | PID: 96A7399A"
        /// </summary>
        public string GetLogSummary()
        {
            var level = this is PartyPokemon party ? $" Lv.{party.Level}" : string.Empty;
            var ivTotal = IVs.HP + IVs.Attack + IVs.Defense + IVs.SpAttack + IVs.SpDefense + IVs.Speed;
            return string.Join(" | ",
                $"{RealName}{level}",
                IsShiny ? Messages.Viewer_Shiny.ToUpperInvariant() : Messages.Viewer_NotShiny,
                $"{Messages.Viewer_IVsGroup} {IVs.HP}/{IVs.Attack}/{IVs.Defense}/{IVs.SpAttack}/{IVs.SpDefense}/{IVs.Speed} = {ivTotal}",
                string.Format(Messages.Viewer_Nature, Nature?.Name ?? "N/A"),
                string.Format(Messages.Viewer_Ability, Ability ?? "N/A"),
                GetGenderMessage(),
                string.Format(Messages.Viewer_PID, PID.ToString("X8")));
        }

        public string GetGenderMessage()
        {
            switch (Gender)
            {
                case PokemonGender.Male: return Messages.Gender_Male;
                case PokemonGender.Female: return Messages.Gender_Female;
                case PokemonGender.Genderless: return Messages.Gender_Genderless;
                default: return Messages.Gender_Unknow;
            }
        }
    }
}
