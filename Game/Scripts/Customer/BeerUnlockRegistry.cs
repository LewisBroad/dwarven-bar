using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Scene-level list of beers that customers are currently allowed to order.</summary>
public class BeerUnlockRegistry : MonoBehaviour
{
    [Serializable]
    public class BeerOption
    {
        public string beerName = "Dwarven Stout";
        public bool unlocked = true;
    }

    [SerializeField] private List<BeerOption> beers = new();
    public static BeerUnlockRegistry Instance { get; private set; }

    private void Awake() => Instance = this;

    public bool TryGetRandomUnlockedBeer(out string beerName)
    {
        List<BeerOption> available = beers.FindAll(beer => beer.unlocked && !string.IsNullOrWhiteSpace(beer.beerName));
        if (available.Count == 0)
        {
            beerName = null;
            return false;
        }

        beerName = available[UnityEngine.Random.Range(0, available.Count)].beerName;
        return true;
    }
}
