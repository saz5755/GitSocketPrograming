using System;
using System.Collections.Generic;
using UnityEngine;

public class UnityMainThreadDispatcher : MonoBehaviour
{
    static readonly Queue<Action> executionQueue = new();

    public static UnityMainThreadDispatcher Instance;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
    }

    public void Enqueue(Action action)
    {
        lock (executionQueue)
        {
            executionQueue.Enqueue(action);
        }
    }

    // 실행은 락 밖에서 — 핸들러(스폰·씬 오브젝트 생성 등)가 오래 걸려도 수신 스레드의 Enqueue가 막히지 않는다.
    // 핸들러 하나가 예외를 던져도 나머지 패킷은 이번 프레임에 계속 처리한다.
    readonly List<Action> _running = new(64);

    void Update()
    {
        lock (executionQueue)
        {
            while (executionQueue.Count > 0)
                _running.Add(executionQueue.Dequeue());
        }

        for (int i = 0; i < _running.Count; i++)
        {
            try { _running[i].Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }
        _running.Clear();
    }
}
