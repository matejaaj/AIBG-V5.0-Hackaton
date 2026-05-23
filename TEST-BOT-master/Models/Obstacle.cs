namespace SharedLibrary;

public class Obstacle
{
    public ObstacleType Type { get; set; }
    public int Damage { get; set; }
    public int Cooldown { get; set; }
    public int CurrentCooldown { get; set; }
    
    public Obstacle(ObstacleType type, int damage, int cooldown = 0)
    {
        Type = type;
        Damage = damage;
        Cooldown = cooldown;
        CurrentCooldown = 0;
    }
    
    public bool IsOnCooldown()
    {
        CurrentCooldown++;
        return CurrentCooldown >= Cooldown;
    }
}

public enum ObstacleType 
{
    Spikes,
    FallingStone,
}