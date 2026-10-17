using System;

namespace Kesa.Engine.Host
{
    // 생성한 코드: 타입 있는 람다를 받는 인자 수별 오버로드 (spec-gaps G215). 인자는 8개까지.
    public sealed partial class HostModFunctions
    {
        public HostModFunctions Pure<TR>(string signature, string doc, Func<TR> implementation) =>
            Add(signature, doc, HostEffect.Pure, implementation);

        public HostModFunctions Writes(string signature, string doc, Action<HostAccess> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation);

        public HostModFunctions Writes<TR>(string signature, string doc, Func<HostAccess, TR> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation);

        public HostModFunctions Pure<T1, TR>(string signature, string doc, Func<T1, TR> implementation) =>
            Add(signature, doc, HostEffect.Pure, implementation);

        public HostModFunctions Writes<T1>(string signature, string doc, Action<HostAccess, T1> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation);

        public HostModFunctions Writes<T1, TR>(string signature, string doc, Func<HostAccess, T1, TR> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation);

        public HostModFunctions Pure<T1, T2, TR>(string signature, string doc, Func<T1, T2, TR> implementation) =>
            Add(signature, doc, HostEffect.Pure, implementation);

        public HostModFunctions Writes<T1, T2>(string signature, string doc, Action<HostAccess, T1, T2> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation);

        public HostModFunctions Writes<T1, T2, TR>(string signature, string doc, Func<HostAccess, T1, T2, TR> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation);

        public HostModFunctions Pure<T1, T2, T3, TR>(string signature, string doc, Func<T1, T2, T3, TR> implementation) =>
            Add(signature, doc, HostEffect.Pure, implementation);

        public HostModFunctions Writes<T1, T2, T3>(string signature, string doc, Action<HostAccess, T1, T2, T3> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation);

        public HostModFunctions Writes<T1, T2, T3, TR>(string signature, string doc, Func<HostAccess, T1, T2, T3, TR> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation);

        public HostModFunctions Pure<T1, T2, T3, T4, TR>(string signature, string doc, Func<T1, T2, T3, T4, TR> implementation) =>
            Add(signature, doc, HostEffect.Pure, implementation);

        public HostModFunctions Writes<T1, T2, T3, T4>(string signature, string doc, Action<HostAccess, T1, T2, T3, T4> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation);

        public HostModFunctions Writes<T1, T2, T3, T4, TR>(string signature, string doc, Func<HostAccess, T1, T2, T3, T4, TR> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation);

        public HostModFunctions Pure<T1, T2, T3, T4, T5, TR>(string signature, string doc, Func<T1, T2, T3, T4, T5, TR> implementation) =>
            Add(signature, doc, HostEffect.Pure, implementation);

        public HostModFunctions Writes<T1, T2, T3, T4, T5>(string signature, string doc, Action<HostAccess, T1, T2, T3, T4, T5> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation);

        public HostModFunctions Writes<T1, T2, T3, T4, T5, TR>(string signature, string doc, Func<HostAccess, T1, T2, T3, T4, T5, TR> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation);

        public HostModFunctions Pure<T1, T2, T3, T4, T5, T6, TR>(string signature, string doc, Func<T1, T2, T3, T4, T5, T6, TR> implementation) =>
            Add(signature, doc, HostEffect.Pure, implementation);

        public HostModFunctions Writes<T1, T2, T3, T4, T5, T6>(string signature, string doc, Action<HostAccess, T1, T2, T3, T4, T5, T6> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation);

        public HostModFunctions Writes<T1, T2, T3, T4, T5, T6, TR>(string signature, string doc, Func<HostAccess, T1, T2, T3, T4, T5, T6, TR> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation);

        public HostModFunctions Pure<T1, T2, T3, T4, T5, T6, T7, TR>(string signature, string doc, Func<T1, T2, T3, T4, T5, T6, T7, TR> implementation) =>
            Add(signature, doc, HostEffect.Pure, implementation);

        public HostModFunctions Writes<T1, T2, T3, T4, T5, T6, T7>(string signature, string doc, Action<HostAccess, T1, T2, T3, T4, T5, T6, T7> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation);

        public HostModFunctions Writes<T1, T2, T3, T4, T5, T6, T7, TR>(string signature, string doc, Func<HostAccess, T1, T2, T3, T4, T5, T6, T7, TR> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation);

        public HostModFunctions Pure<T1, T2, T3, T4, T5, T6, T7, T8, TR>(string signature, string doc, Func<T1, T2, T3, T4, T5, T6, T7, T8, TR> implementation) =>
            Add(signature, doc, HostEffect.Pure, implementation);

        public HostModFunctions Writes<T1, T2, T3, T4, T5, T6, T7, T8>(string signature, string doc, Action<HostAccess, T1, T2, T3, T4, T5, T6, T7, T8> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation);

        public HostModFunctions Writes<T1, T2, T3, T4, T5, T6, T7, T8, TR>(string signature, string doc, Func<HostAccess, T1, T2, T3, T4, T5, T6, T7, T8, TR> implementation) =>
            Add(signature, doc, HostEffect.Writes, implementation);
    }
}
