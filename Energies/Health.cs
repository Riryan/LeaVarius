using UnityEngine;

public interface IHealthBonus
{
    int GetHealthBonus(int baseHealth);
    int GetHealthRecoveryBonus();
}

[RequireComponent(typeof(Level))]
[DisallowMultipleComponent]
public class Health : Energy
{
    [Header("Refs")]
    public Level level;

    [Header("Base Stats")]
    public LinearInt baseHealth = new LinearInt { baseValue = 100 };

    [Tooltip("Health recovered per Recover() tick before bonuses.")]
    public int baseRecoveryRate = 1;

    [Tooltip("Health drained per Draining() tick before bonuses (e.g., bleed).")]
    public int baseDrainingRate = 1;

    IHealthBonus[] _bonusComponents;
    IHealthBonus[] bonusComponents =>
        _bonusComponents ?? (_bonusComponents = GetComponents<IHealthBonus>());

    public void InvalidateBonusCache() => _bonusComponents = null;

    public override int max
    {
        get
        {
            int baseThisLevel = baseHealth.Get(level != null ? level.current : 0);

            int bonus = 0;
            foreach (IHealthBonus b in bonusComponents)
                bonus += b.GetHealthBonus(baseThisLevel);

            int result = baseThisLevel + bonus;
            return result < 0 ? 0 : result;
        }
    }

    public override int recoveryRate
    {
        get
        {
            int bonus = 0;
            foreach (IHealthBonus b in bonusComponents)
                bonus += b.GetHealthRecoveryBonus();

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

        // keep inputs non-negative to avoid surprises
        if (baseHealth.baseValue < 0) baseHealth.baseValue = 0;
        if (baseRecoveryRate < 0)     baseRecoveryRate   = 0;
        if (baseDrainingRate < 0)     baseDrainingRate   = 0;
    }
}
