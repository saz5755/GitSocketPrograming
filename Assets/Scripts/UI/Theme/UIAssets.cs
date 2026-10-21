using System.Collections;
using UnityEngine;

/// <summary>
/// 씬 사이에서 재사용하는 UI 에셋 캐시 + 첫 사용 시 끊김(hitch)을 막는 예열(prewarm).
/// - 큰 배경 텍스처는 정적 참조로 잡아 두어 씬 전환 시 UnloadUnusedAssets 로 해제/재로딩되지 않게 한다.
/// - 동적 폰트 글리프(한글 포함)는 처음 그려질 때 아틀라스를 만들며 프레임을 잡아먹으므로, 한가한 로그인 화면에서
///   프레임당 한 조합씩 미리 요청해 둔다.
/// </summary>
public static class UIAssets
{
    static Texture2D _loginBg;

    /// <summary>로그인/로비 공용 KF-21 배경 사진 (씬 전환 후에도 유지).</summary>
    public static Texture2D LoginBg
    {
        get
        {
            if (_loginBg == null) _loginBg = Resources.Load<Texture2D>("UI/login_bg");
            return _loginBg;
        }
    }

    // UI 스크립트에 쓰인 한글 음절 (로그인/로비/모달/퀘스트/HUD 문구)
    const string Hangul =
        "가각간감갑값강갖같개갱거건것게겠겨격결경계고곧곳공과곽관광구국굳귀규그근글금긍기길김깊까깥께꽃꽉꾼끝낌나난날내낸너넌널넘네노녹높누눈눌뉴느는늘능니닉다단닫달당대더덮데델도독돌동되된됨두둔둡둥뒤드든들등디딩따때떠떤뛰뜻띄띠라락래램량러럼럽럿레렌려력렬로록롤료루룸룹른를름리릭린림링마막만말맞매맥머멀메며면명모목못문므미민밀바박반밝방배백버번범법벗변별보복본부분불붉붙뷰블비빈빌빛빠뻗뿜사산살상새색생서석선설섬성세섹셀셉셋션셰소속손솔수순숨쉬스슬습승시식신실심씨씬아안않알암앙애액앵약양어얼업없에엔여역연열영옅오와완왕외왼요용우운움웃워원위유윤율으은을음의이인일읽임입있자작잘잠장재저적전절점접정제조존종좌주준줄줌중즉즐증지직진질짝쩍쪽차참채처천첫체초최추춘출충춰취측치칙칠침칩카캐캔캡커컨컬컴코콕콘콜크큰클키킨타탑태택탭터테텍템토톤톨통투트튼틀티틴틸팅파패팹퍼평포폭폰폴폼표푸프플피픽필핏하한함합항해행향헤헬현형호홀화확환활황회후흐흔흩흰히";
    const string Ascii = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~·…—•●○◉►▶↻✕";

    /// <summary>
    /// 공용 UI 예열: 둥근 모서리 스프라이트, 글로우 텍스처, 배경 사진. (폰트 글리프 예열은 프로파일 결과 오히려 프레임을 튀게 해서 제거)
    /// 한 번에 몰아서 하지 않고 프레임에 나눠 실행하므로 코루틴으로 돌린다.
    /// </summary>
    public static IEnumerator Prewarm(Font font)
    {
        // 로그인 첫 화면의 등장 애니메이션이 끝난 뒤(한가할 때) 시작한다
        yield return new WaitForSecondsRealtime(1.5f);

        // 1) 로비/모달에서 쓰는 스프라이트·텍스처
        var _ = LoginBg;
        foreach (int r in new[] { 3, 4, 5, 6, 8, 10, 12, 13, 15, 16, 18, 24, 26, 28 })
        {
            UIShapes.Fill(r); UIShapes.Ring(r);
        }
        var __ = UIGlowKit.Radial; var ___ = UIGlowKit.Streak; var ____ = UIGlowKit.Additive;
        yield return null;

    }
}
