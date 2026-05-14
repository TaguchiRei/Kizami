using System;
using UnityEngine;

public class SceneRule : MonoBehaviour
{
    [SerializeReference, SubclassSelector] private IRule[] _rules;

    private Action _updates;

    private void Start()
    {
        foreach (var rule in _rules)
        {
            if (rule is IUpdateRule updateRule)
            {
                _updates += updateRule.Update;
            }

            rule.OnGameEndAction += OnGameEnd;

            rule.StartGame();
        }
    }

    private void Update()
    {
        _updates?.Invoke();
    }

    private void OnGameEnd(RuleState state)
    {
        foreach (var rule in _rules)
        {
            rule.Stop();
        }
    }
}