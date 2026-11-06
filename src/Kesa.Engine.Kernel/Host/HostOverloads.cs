using System;

namespace Kesa.Engine.Host
{
    // 생성한 코드: 타입 있는 람다를 받는 인자 수별 오버로드 (spec-gaps G215). 인자는 8개까지.
    // 호출 람다는 형식을 아는 여기서 만들어 호출마다 리플렉션을 타지 않게 한다.
    public sealed partial class HostModFunctions
    {
        private static T Arg<T>(object?[] args, int index) => args[index] == null ? default! : (T)args[index]!;

        public HostModFunctions Pure<TR>(string signature, string doc, Func<TR> implementation) =>
            Add(signature, doc, HostEffect.Pure, implementation, a => implementation());

        public HostModFunctions Writes(string signature, string doc, Action<HostAccess> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation, a => { implementation((HostAccess)a[0]!); return null; });

        public HostModFunctions Writes<TR>(string signature, string doc, Func<HostAccess, TR> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation, a => implementation((HostAccess)a[0]!));

        public HostModFunctions Pure<T1, TR>(string signature, string doc, Func<T1, TR> implementation) =>
            Add(signature, doc, HostEffect.Pure, implementation, a => implementation(Arg<T1>(a, 0)));

        public HostModFunctions Writes<T1>(string signature, string doc, Action<HostAccess, T1> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation, a => { implementation((HostAccess)a[0]!, Arg<T1>(a, 1)); return null; });

        public HostModFunctions Writes<T1, TR>(string signature, string doc, Func<HostAccess, T1, TR> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation, a => implementation((HostAccess)a[0]!, Arg<T1>(a, 1)));

        public HostModFunctions Pure<T1, T2, TR>(string signature, string doc, Func<T1, T2, TR> implementation) =>
            Add(signature, doc, HostEffect.Pure, implementation, a => implementation(Arg<T1>(a, 0), Arg<T2>(a, 1)));

        public HostModFunctions Writes<T1, T2>(string signature, string doc, Action<HostAccess, T1, T2> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation, a => { implementation((HostAccess)a[0]!, Arg<T1>(a, 1), Arg<T2>(a, 2)); return null; });

        public HostModFunctions Writes<T1, T2, TR>(string signature, string doc, Func<HostAccess, T1, T2, TR> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation, a => implementation((HostAccess)a[0]!, Arg<T1>(a, 1), Arg<T2>(a, 2)));

        public HostModFunctions Pure<T1, T2, T3, TR>(string signature, string doc, Func<T1, T2, T3, TR> implementation) =>
            Add(signature, doc, HostEffect.Pure, implementation, a => implementation(Arg<T1>(a, 0), Arg<T2>(a, 1), Arg<T3>(a, 2)));

        public HostModFunctions Writes<T1, T2, T3>(string signature, string doc, Action<HostAccess, T1, T2, T3> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation, a => { implementation((HostAccess)a[0]!, Arg<T1>(a, 1), Arg<T2>(a, 2), Arg<T3>(a, 3)); return null; });

        public HostModFunctions Writes<T1, T2, T3, TR>(string signature, string doc, Func<HostAccess, T1, T2, T3, TR> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation, a => implementation((HostAccess)a[0]!, Arg<T1>(a, 1), Arg<T2>(a, 2), Arg<T3>(a, 3)));

        public HostModFunctions Pure<T1, T2, T3, T4, TR>(string signature, string doc, Func<T1, T2, T3, T4, TR> implementation) =>
            Add(signature, doc, HostEffect.Pure, implementation, a => implementation(Arg<T1>(a, 0), Arg<T2>(a, 1), Arg<T3>(a, 2), Arg<T4>(a, 3)));

        public HostModFunctions Writes<T1, T2, T3, T4>(string signature, string doc, Action<HostAccess, T1, T2, T3, T4> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation, a => { implementation((HostAccess)a[0]!, Arg<T1>(a, 1), Arg<T2>(a, 2), Arg<T3>(a, 3), Arg<T4>(a, 4)); return null; });

        public HostModFunctions Writes<T1, T2, T3, T4, TR>(string signature, string doc, Func<HostAccess, T1, T2, T3, T4, TR> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation, a => implementation((HostAccess)a[0]!, Arg<T1>(a, 1), Arg<T2>(a, 2), Arg<T3>(a, 3), Arg<T4>(a, 4)));

        public HostModFunctions Pure<T1, T2, T3, T4, T5, TR>(string signature, string doc, Func<T1, T2, T3, T4, T5, TR> implementation) =>
            Add(signature, doc, HostEffect.Pure, implementation, a => implementation(Arg<T1>(a, 0), Arg<T2>(a, 1), Arg<T3>(a, 2), Arg<T4>(a, 3), Arg<T5>(a, 4)));

        public HostModFunctions Writes<T1, T2, T3, T4, T5>(string signature, string doc, Action<HostAccess, T1, T2, T3, T4, T5> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation, a => { implementation((HostAccess)a[0]!, Arg<T1>(a, 1), Arg<T2>(a, 2), Arg<T3>(a, 3), Arg<T4>(a, 4), Arg<T5>(a, 5)); return null; });

        public HostModFunctions Writes<T1, T2, T3, T4, T5, TR>(string signature, string doc, Func<HostAccess, T1, T2, T3, T4, T5, TR> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation, a => implementation((HostAccess)a[0]!, Arg<T1>(a, 1), Arg<T2>(a, 2), Arg<T3>(a, 3), Arg<T4>(a, 4), Arg<T5>(a, 5)));

        public HostModFunctions Pure<T1, T2, T3, T4, T5, T6, TR>(string signature, string doc, Func<T1, T2, T3, T4, T5, T6, TR> implementation) =>
            Add(signature, doc, HostEffect.Pure, implementation, a => implementation(Arg<T1>(a, 0), Arg<T2>(a, 1), Arg<T3>(a, 2), Arg<T4>(a, 3), Arg<T5>(a, 4), Arg<T6>(a, 5)));

        public HostModFunctions Writes<T1, T2, T3, T4, T5, T6>(string signature, string doc, Action<HostAccess, T1, T2, T3, T4, T5, T6> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation, a => { implementation((HostAccess)a[0]!, Arg<T1>(a, 1), Arg<T2>(a, 2), Arg<T3>(a, 3), Arg<T4>(a, 4), Arg<T5>(a, 5), Arg<T6>(a, 6)); return null; });

        public HostModFunctions Writes<T1, T2, T3, T4, T5, T6, TR>(string signature, string doc, Func<HostAccess, T1, T2, T3, T4, T5, T6, TR> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation, a => implementation((HostAccess)a[0]!, Arg<T1>(a, 1), Arg<T2>(a, 2), Arg<T3>(a, 3), Arg<T4>(a, 4), Arg<T5>(a, 5), Arg<T6>(a, 6)));

        public HostModFunctions Pure<T1, T2, T3, T4, T5, T6, T7, TR>(string signature, string doc, Func<T1, T2, T3, T4, T5, T6, T7, TR> implementation) =>
            Add(signature, doc, HostEffect.Pure, implementation, a => implementation(Arg<T1>(a, 0), Arg<T2>(a, 1), Arg<T3>(a, 2), Arg<T4>(a, 3), Arg<T5>(a, 4), Arg<T6>(a, 5), Arg<T7>(a, 6)));

        public HostModFunctions Writes<T1, T2, T3, T4, T5, T6, T7>(string signature, string doc, Action<HostAccess, T1, T2, T3, T4, T5, T6, T7> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation, a => { implementation((HostAccess)a[0]!, Arg<T1>(a, 1), Arg<T2>(a, 2), Arg<T3>(a, 3), Arg<T4>(a, 4), Arg<T5>(a, 5), Arg<T6>(a, 6), Arg<T7>(a, 7)); return null; });

        public HostModFunctions Writes<T1, T2, T3, T4, T5, T6, T7, TR>(string signature, string doc, Func<HostAccess, T1, T2, T3, T4, T5, T6, T7, TR> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation, a => implementation((HostAccess)a[0]!, Arg<T1>(a, 1), Arg<T2>(a, 2), Arg<T3>(a, 3), Arg<T4>(a, 4), Arg<T5>(a, 5), Arg<T6>(a, 6), Arg<T7>(a, 7)));

        public HostModFunctions Pure<T1, T2, T3, T4, T5, T6, T7, T8, TR>(string signature, string doc, Func<T1, T2, T3, T4, T5, T6, T7, T8, TR> implementation) =>
            Add(signature, doc, HostEffect.Pure, implementation, a => implementation(Arg<T1>(a, 0), Arg<T2>(a, 1), Arg<T3>(a, 2), Arg<T4>(a, 3), Arg<T5>(a, 4), Arg<T6>(a, 5), Arg<T7>(a, 6), Arg<T8>(a, 7)));

        public HostModFunctions Writes<T1, T2, T3, T4, T5, T6, T7, T8>(string signature, string doc, Action<HostAccess, T1, T2, T3, T4, T5, T6, T7, T8> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation, a => { implementation((HostAccess)a[0]!, Arg<T1>(a, 1), Arg<T2>(a, 2), Arg<T3>(a, 3), Arg<T4>(a, 4), Arg<T5>(a, 5), Arg<T6>(a, 6), Arg<T7>(a, 7), Arg<T8>(a, 8)); return null; });

        public HostModFunctions Writes<T1, T2, T3, T4, T5, T6, T7, T8, TR>(string signature, string doc, Func<HostAccess, T1, T2, T3, T4, T5, T6, T7, T8, TR> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation, a => implementation((HostAccess)a[0]!, Arg<T1>(a, 1), Arg<T2>(a, 2), Arg<T3>(a, 3), Arg<T4>(a, 4), Arg<T5>(a, 5), Arg<T6>(a, 6), Arg<T7>(a, 7), Arg<T8>(a, 8)));
    }
}
