﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="InspectorIntAttribute.cs" company="Slash Games">
//   Copyright (c) Slash Games. All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

namespace Slash.ECS.Inspector.Attributes
{
    using System;
    using System.Collections;
    using System.Collections.Generic;

    using Slash.ECS.Inspector.Validation;

    /// <summary>
    ///   Exposes the property to the inspector.
    /// </summary>
    [Serializable]
    public class InspectorIntAttribute : InspectorPropertyAttribute
    {
        #region Constants

        /// <summary>
        ///   Validation message to use for strings which are too long.
        /// </summary>
        private const string ValidationMessageOutOfRange = "Value is out of range (min: {0}, max: {1}).";

        #endregion

        #region Constructors and Destructors

        /// <summary>
        ///   Exposes the property to the inspector.
        /// </summary>
        /// <param name="name">Property name to be shown in the inspector.</param>
        public InspectorIntAttribute(string name)
        {
            this.Min = int.MinValue;
            this.Max = int.MaxValue;
            this.Name = name;
        }

        /// <summary>
        ///   Exposes the property to the inspector.
        /// </summary>
        public InspectorIntAttribute()
        {
        }

        #endregion

        #region Properties

        /// <summary>
        ///   Maximum property value (inclusive).
        /// </summary>
        public int Max { get; set; }

        /// <summary>
        ///   Minimum property value (inclusive).
        /// </summary>
        public int Min { get; set; }

        #endregion

        #region Public Methods and Operators

        /// <summary>
        ///   Converts the passed text to a value of the correct type for this property.
        /// </summary>
        /// <param name="text">Text to convert.</param>
        /// <returns>
        ///   Value of the correct type for this property, if the conversion was successful, and <c>null</c> otherwise.
        /// </returns>
        public override object ConvertStringToValue(string text)
        {
            return text == string.Empty ? 0 : int.Parse(text);
        }

        /// <summary>
        ///   Converts the passed value to a string that can be converted back to a value of the correct type for this property.
        /// </summary>
        /// <param name="value">Value to convert.</param>
        /// <returns>String that can be converted back to a value of the correct type for this property.</returns>
        /// <see cref="InspectorPropertyAttribute.ConvertStringToValue" />
        public override string ConvertValueToString(object value)
        {
            return value.ToString();
        }

        /// <summary>
        ///   Gets an empty list for elements of the type of the property the attribute is attached to.
        /// </summary>
        /// <returns>Empty list of matching type.</returns>
        public override IList GetEmptyList()
        {
            return new List<int>();
        }

        /// <summary>
        ///   Returns a string that represents the current object.
        /// </summary>
        /// <returns>
        ///   A string that represents the current object.
        /// </returns>
        /// <filterpriority>2</filterpriority>
        public override string ToString()
        {
            return string.Format(
                "Name: {0}, Max: {1}, Min: {2}, Default: {3}",
                this.Name,
                this.Max,
                this.Min,
                this.Default);
        }

        /// <summary>
        ///   Tries to convert the specified text to a value of the correct type for this property.
        /// </summary>
        /// <param name="text">Text to convert.</param>
        /// <param name="value">Value of the correct type for this property, if the conversion was successful.</param>
        /// <returns>
        ///   True if the conversion was successful; otherwise, false.
        /// </returns>
        public override bool TryConvertStringToValue(string text, out object value)
        {
            int intValue = 0;

            // Treat empty string as 0.
            bool success = text == string.Empty || int.TryParse(text, out intValue);
            value = intValue;
            return success;
        }

        /// <summary>
        ///   Tries to convert the specified value to a string that can be converted back to a value of the correct type for this
        ///   property.
        /// </summary>
        /// <param name="value">Value to convert.</param>
        /// <param name="text">String that can be converted back to a value of the correct type for this property.</param>
        /// <see cref="InspectorPropertyAttribute.TryConvertStringToValue" />
        /// <returns>
        ///   True if the conversion was successful; otherwise, false.
        /// </returns>
        public override bool TryConvertValueToString(object value, out string text)
        {
            text = value.ToString();
            return true;
        }

        /// <summary>
        ///   Checks whether the passed value is valid for this property.
        /// </summary>
        /// <param name="value">Value to check.</param>
        /// <returns>
        ///   <c>null</c>, if the passed value is valid for this property,
        ///   and <see cref="ValidationError" /> which contains information about the error otherwise.
        /// </returns>
        public override ValidationError Validate(object value)
        {
            if (value == null)
            {
                return ValidationError.Null;
            }

            if (!(value is int))
            {
                return ValidationError.WrongType;
            }

            int intValue = (int)value;
            if (intValue < this.Min || intValue > this.Max)
            {
                return new ValidationError { Message = string.Format(ValidationMessageOutOfRange, this.Min, this.Max) };
            }

            return null;
        }

        #endregion
    }
}