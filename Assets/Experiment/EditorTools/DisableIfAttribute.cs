using System;
using UnityEngine;

namespace InspectorTools {
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    public class DisableIfAttribute : PropertyAttribute {
        public string conditionField;
        public object compareValue;

        public DisableIfAttribute(string conditionField, object compareValue) {
            this.conditionField = conditionField;
            this.compareValue = compareValue;
        }
    }
}
