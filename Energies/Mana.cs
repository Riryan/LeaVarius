using UnityEngine;

public interface IManaBonus
{
    int GetManaBonus(int baseMana);
    int GetManaRecoveryBonus();
}

[RequireComponent(typeof(Level))]
[DisallowMultipleComponent]
public class Mana : Energy
{
    [Header("Refs")]
    public Level level;

    [Header("Base Stats")]
    public LinearInt baseMana = new LinearInt { baseValue = 100 };

    [Tooltip("Mana recovered per Recover() tick before bonuses.")]
    public int baseRecoveryRate = 1;

    [Tooltip("Mana drained per Draining() tick before bonuses (e.g., channeling).")]
    public int baseDrainingRate = 1;

    IManaBonus[] _bonusComponents;
    IManaBonus[] bonusComponents =>
        _bonusComponents ?? (_bonusComponents = GetComponents<IManaBonus>());

    public void InvalidateBonusCache() => _bonusComponents = null;

    public override int max
    {
        get
        {
            int lvl = level != null ? level.current : 0;
            int baseThisLevel = baseMana.Get(lvl);

            int bonus = 0;
            foreach (IManaBonus b in bonusComponents)
                bonus += b.GetManaBonus(baseThisLevel);

            int result = baseThisLevel + bonus;
            return result < 0 ? 0 : result;
        }
    }

    public override int recoveryRate
    {
        get
        {
            int bonus = 0;
            foreach (IManaBonus b in bonusComponents)
                bonus += b.GetManaRecoveryBonus();

            int rate = baseRecoveryRate + bonus;
            return rate < 0 ? 0 : rate;
        }
    }

    public override int drainRate
    {
        get
        {
            return baseDrainingRate < 0 ? 0 : baseDrainingRate;
        }
    }

    void OnValidate()
    {
        if (level == null) level = GetComponent<Level>();

        if (baseMana.baseValue   < 0) baseMana.baseValue   = 0;
        if (baseRecoveryRate     < 0) baseRecoveryRate     = 0;
        if (baseDrainingRate     < 0) baseDrainingRate     = 0;
    }
}
