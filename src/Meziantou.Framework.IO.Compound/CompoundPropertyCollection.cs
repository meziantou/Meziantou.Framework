using System;
using System.Collections;
using System.Collections.Generic;

namespace Meziantou.Framework.IO.Compound
{
    /// <summary>
    /// Represents a list of Compound file properties.
    /// </summary>
    public sealed class CompoundPropertyCollection : IList<CompoundProperty>
    {
        internal bool _readOnly;
        private readonly List<CompoundProperty> _list = new List<CompoundProperty>();
        internal List<CompoundProperty> _deleted = new List<CompoundProperty>();

        internal CompoundPropertyCollection(bool readOnly)
        {
            _readOnly = readOnly;
        }

        internal void Commit()
        {
            foreach (var property in _deleted)
            {
                property.Deleted = false;
                property.Changed = false;
            }
            _deleted.Clear();
            foreach (var property in _list)
            {
                property.Deleted = false;
                property.Changed = false;
            }
        }

        internal void InternalAdd(CompoundProperty item)
        {
            CompoundProperty? existing = null;
            foreach (var property in this)
            {
                if (property.FormatId == item.FormatId &&
                    property.Id == item.Id &&
                    property.Name == item.Name)
                {
                    existing = property;
                    break;
                }
            }

            if (existing != null)
                Remove(existing);
            _list.Add(item);
        }

        /// <summary>
        /// Determines the index of a specific item in the <see cref="T:System.Collections.Generic.IList`1"/>.
        /// </summary>
        /// <param name="item">The object to locate in the <see cref="T:System.Collections.Generic.IList`1"/>.</param>
        /// <returns>
        /// The index of <paramref name="item"/> if found in the list; otherwise, -1.
        /// </returns>
        public int IndexOf(CompoundProperty item)
        {
            return _list.IndexOf(item);
        }

        /// <summary>
        /// Inserts an item to the <see cref="T:System.Collections.Generic.IList`1"/> at the specified index.
        /// </summary>
        /// <param name="index">The zero-based index at which <paramref name="item"/> should be inserted.</param>
        /// <param name="item">The object to insert into the <see cref="T:System.Collections.Generic.IList`1"/>.</param>
        public void Insert(int index, CompoundProperty item)
        {
            if (_readOnly)
                throw new CompoundReadOnlyException();

            _list.Insert(index, item);
        }

        /// <summary>
        /// Removes the <see cref="T:System.Collections.Generic.IList`1"/> item at the specified index.
        /// </summary>
        /// <param name="index">The zero-based index of the item to remove.</param>
        public void RemoveAt(int index)
        {
            if (_readOnly)
                throw new CompoundReadOnlyException();

            this[index].Deleted = true;
            _deleted.Add(this[index]);
            _list.RemoveAt(index);
        }

        /// <summary>
        /// Sets a property value.
        /// </summary>
        /// <param name="formatId">The format id.</param>
        /// <param name="name">The name. May be null.</param>
        /// <param name="value">The value. May be null.</param>
        public void SetValue(Guid formatId, string? name, object? value)
        {
            if (_readOnly)
                throw new CompoundReadOnlyException();

            var property = this[formatId, name];
            if (property == null)
            {
                property = new CompoundProperty(formatId, name);
                Add(property);
            }
            property.Value = value;
        }

        /// <summary>
        /// Gets a property value.
        /// </summary>
        /// <param name="formatId">The format id.</param>
        /// <param name="name">The name. May be null.</param>
        /// <param name="defaultValue">The default value to use if the property was not found.</param>
        /// <returns>The property value.</returns>
        public object? GetValue(Guid formatId, string? name, object? defaultValue)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));

            var property = this[formatId, name];
            if (property == null)
                return defaultValue;

            return property.Value;
        }

        /// <summary>
        /// Gets a property value.
        /// </summary>
        /// <param name="formatId">The format id.</param>
        /// <param name="name">The name. May be null.</param>
        /// <param name="defaultValue">The default value to use if the property was not found.</param>
        /// <returns>The property value.</returns>
        public T GetValue<T>(Guid formatId, string? name, T defaultValue)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));

            var property = this[formatId, name];
            if (property == null)
                return defaultValue;

            return ConvertUtilities.ChangeType(property.Value, defaultValue);
        }

        /// <summary>
        /// Gets the <see cref="CompoundProperty"/> with the specified format id and name.
        /// </summary>
        /// <value></value>
        public CompoundProperty? this[Guid formatId, string? name]
        {
            get
            {
                return _list.Find(p => p.Name == name && p.FormatId == formatId);
            }
        }

        /// <summary>
        /// Gets or sets the <see cref="CompoundProperty"/> at the specified index.
        /// </summary>
        /// <value></value>
        public CompoundProperty this[int index]
        {
            get => _list[index];
            set
            {
                if (_readOnly)
                    throw new CompoundReadOnlyException();

                _list[index] = value;
            }
        }

        /// <summary>
        /// Adds an item to the <see cref="T:System.Collections.Generic.ICollection`1"/>.
        /// </summary>
        /// <param name="item">The object to add to the <see cref="T:System.Collections.Generic.ICollection`1"/>.</param>
        public void Add(CompoundProperty item)
        {
            if (_readOnly)
                throw new CompoundReadOnlyException();

            _list.Add(item);
        }

        /// <summary>
        /// Removes all items from the <see cref="T:System.Collections.Generic.ICollection`1"/>.
        /// </summary>
        public void Clear()
        {
            if (_readOnly)
                throw new CompoundReadOnlyException();

            _deleted.AddRange(_list);
            _list.Clear();
        }

        /// <summary>
        /// Determines whether the <see cref="T:System.Collections.Generic.ICollection`1"/> contains a specific value.
        /// </summary>
        /// <param name="item">The object to locate in the <see cref="T:System.Collections.Generic.ICollection`1"/>.</param>
        /// <returns>
        /// true if <paramref name="item"/> is found in the <see cref="T:System.Collections.Generic.ICollection`1"/>; otherwise, false.
        /// </returns>
        public bool Contains(CompoundProperty item)
        {
            return _list.Contains(item);
        }

        /// <summary>
        /// Copies the elements of the <see cref="T:System.Collections.Generic.ICollection`1"/> to an <see cref="T:System.Array"/>, starting at a particular <see cref="T:System.Array"/> index.
        /// </summary>
        /// <param name="array">The one-dimensional <see cref="T:System.Array"/> that is the destination of the elements copied from <see cref="T:System.Collections.Generic.ICollection`1"/>. The <see cref="T:System.Array"/> must have zero-based indexing.</param>
        /// <param name="arrayIndex">The zero-based index in <paramref name="array"/> at which copying begins.</param>
        public void CopyTo(CompoundProperty[] array, int arrayIndex)
        {
            _list.CopyTo(array, arrayIndex);
        }

        /// <summary>
        /// Gets the number of elements contained in the <see cref="T:System.Collections.Generic.ICollection`1"/>.
        /// </summary>
        /// <value></value>
        /// <returns>The number of elements contained in the <see cref="T:System.Collections.Generic.ICollection`1"/>.</returns>
        public int Count => _list.Count;

        /// <summary>
        /// Gets a value indicating whether the <see cref="T:System.Collections.Generic.ICollection`1"/> is read-only.
        /// </summary>
        /// <value></value>
        /// <returns>true if the <see cref="T:System.Collections.Generic.ICollection`1"/> is read-only; otherwise, false.</returns>
        public bool IsReadOnly => _readOnly;

        /// <summary>
        /// Removes the first occurrence of a specific object from the <see cref="T:System.Collections.Generic.ICollection`1"/>.
        /// </summary>
        /// <param name="item">The object to remove from the <see cref="T:System.Collections.Generic.ICollection`1"/>.</param>
        /// <returns>
        /// true if <paramref name="item"/> was successfully removed from the <see cref="T:System.Collections.Generic.ICollection`1"/>; otherwise, false. This method also returns false if <paramref name="item"/> is not found in the original <see cref="T:System.Collections.Generic.ICollection`1"/>.
        /// </returns>
        public bool Remove(CompoundProperty item)
        {
            if (_readOnly)
                throw new CompoundReadOnlyException();

            if (item == null)
                throw new ArgumentNullException(nameof(item));

            item.Deleted = true;
            _deleted.Add(item);
            return _list.Remove(item);
        }

        /// <summary>
        /// Returns an enumerator that iterates through the collection.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.Collections.Generic.IEnumerator`1"/> that can be used to iterate through the collection.
        /// </returns>
        public IEnumerator<CompoundProperty> GetEnumerator()
        {
            return _list.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Gets or sets the category.
        /// </summary>
        /// <value>The category.</value>
        public string? Category
        {
            get => GetValue(CompoundProperty.DocSummaryInformationFormatId, "Category", (string?)null);
            set => SetValue(CompoundProperty.DocSummaryInformationFormatId, "Category", value);
        }

        /// <summary>
        /// Gets or sets the presentation target.
        /// </summary>
        /// <value>The presentation target.</value>
        public string? PresentationTarget
        {
            get => GetValue(CompoundProperty.DocSummaryInformationFormatId, "PresentationTarget", (string?)null);
            set => SetValue(CompoundProperty.DocSummaryInformationFormatId, "PresentationTarget", value);
        }

        /// <summary>
        /// Gets or sets the manager.
        /// </summary>
        /// <value>The manager.</value>
        public string? Manager
        {
            get => GetValue(CompoundProperty.DocSummaryInformationFormatId, "Manager", (string?)null);
            set => SetValue(CompoundProperty.DocSummaryInformationFormatId, "Manager", value);
        }

        /// <summary>
        /// Gets or sets the company.
        /// </summary>
        /// <value>The company.</value>
        public string? Company
        {
            get => GetValue(CompoundProperty.DocSummaryInformationFormatId, "Company", (string?)null);
            set => SetValue(CompoundProperty.DocSummaryInformationFormatId, "Company", value);
        }

        /// <summary>
        /// Gets or sets the bytes count.
        /// </summary>
        /// <value>The bytes count.</value>
        public int? Bytes
        {
            get => GetValue(CompoundProperty.DocSummaryInformationFormatId, "Bytes", (int?)null);
            set => SetValue(CompoundProperty.DocSummaryInformationFormatId, "Bytes", value);
        }

        /// <summary>
        /// Gets or sets the lines count.
        /// </summary>
        /// <value>The lines count.</value>
        public int? Lines
        {
            get => GetValue(CompoundProperty.DocSummaryInformationFormatId, "Lines", (int?)null);
            set => SetValue(CompoundProperty.DocSummaryInformationFormatId, "Lines", value);
        }

        /// <summary>
        /// Gets or sets the paragraphs count.
        /// </summary>
        /// <value>The paragraphs count.</value>
        public int? Paragraphs
        {
            get => GetValue(CompoundProperty.DocSummaryInformationFormatId, "Paragraphs", (int?)null);
            set => SetValue(CompoundProperty.DocSummaryInformationFormatId, "Paragraphs", value);
        }

        /// <summary>
        /// Gets or sets the slides count.
        /// </summary>
        /// <value>The slides count.</value>
        public int? Slides
        {
            get => GetValue(CompoundProperty.DocSummaryInformationFormatId, "Slides", (int?)null);
            set => SetValue(CompoundProperty.DocSummaryInformationFormatId, "Slides", value);
        }

        /// <summary>
        /// Gets or sets the hidden slides count.
        /// </summary>
        /// <value>The hidden slides count.</value>
        public int? HiddenSlides
        {
            get => GetValue(CompoundProperty.DocSummaryInformationFormatId, "HiddenSlides", (int?)null);
            set => SetValue(CompoundProperty.DocSummaryInformationFormatId, "HiddenSlides", value);
        }

        /// <summary>
        /// Gets or sets the multimedia clips count.
        /// </summary>
        /// <value>The multimedia clips count.</value>
        public int? MMClips
        {
            get => GetValue(CompoundProperty.DocSummaryInformationFormatId, "MMClips", (int?)null);
            set => SetValue(CompoundProperty.DocSummaryInformationFormatId, "MMClips", value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether to scale crop.
        /// </summary>
        /// <value><c>true</c> if the document is scale cropped; otherwise, <c>false</c>.</value>
        public bool? ScaleCrop
        {
            get => GetValue(CompoundProperty.DocSummaryInformationFormatId, "ScaleCrop", (bool?)null);
            set => SetValue(CompoundProperty.DocSummaryInformationFormatId, "ScaleCrop", value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether the document is shared.
        /// </summary>
        /// <value><c>true</c> if the document is shared; otherwise, <c>false</c>.</value>
        public bool? SharedDoc
        {
            get => GetValue(CompoundProperty.DocSummaryInformationFormatId, "SharedDoc", (bool?)null);
            set => SetValue(CompoundProperty.DocSummaryInformationFormatId, "SharedDoc", value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether hyperlinks have changed.
        /// </summary>
        /// <value><c>true</c> if hyperlinks have changed; otherwise, <c>false</c>.</value>
        public bool? HyperLinksChanged
        {
            get => GetValue(CompoundProperty.DocSummaryInformationFormatId, "HyperLinksChanged", (bool?)null);
            set => SetValue(CompoundProperty.DocSummaryInformationFormatId, "HyperLinksChanged", value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether links are up-to-date.
        /// </summary>
        /// <value><c>true</c> if links are up-to-date; otherwise, <c>false</c>.</value>
        public bool? LinksUpToDate
        {
            get => GetValue(CompoundProperty.DocSummaryInformationFormatId, "LinksUpToDate", (bool?)null);
            set => SetValue(CompoundProperty.DocSummaryInformationFormatId, "LinksUpToDate", value);
        }

        /// <summary>
        /// Gets or sets the notes count.
        /// </summary>
        /// <value>The notes count.</value>
        public int? Notes
        {
            get => GetValue(CompoundProperty.DocSummaryInformationFormatId, "Notes", (int?)null);
            set => SetValue(CompoundProperty.DocSummaryInformationFormatId, "Notes", value);
        }

        /// <summary>
        /// Gets or sets the author.
        /// </summary>
        /// <value>The author.</value>
        public string? Author
        {
            get => GetValue(CompoundProperty.SummaryInformationFormatId, "Author", (string?)null);
            set => SetValue(CompoundProperty.SummaryInformationFormatId, "Author", value);
        }

        /// <summary>
        /// Gets or sets the name of the last author.
        /// </summary>
        /// <value>The name of the last author.</value>
        public string? LastSavedBy
        {
            get => GetValue(CompoundProperty.SummaryInformationFormatId, "LastSavedBy", (string?)null);
            set => SetValue(CompoundProperty.SummaryInformationFormatId, "LastSavedBy", value);
        }

        /// <summary>
        /// Gets or sets the revision number.
        /// </summary>
        /// <value>The revision number.</value>
        public string? RevisionNumber
        {
            get => GetValue(CompoundProperty.SummaryInformationFormatId, "RevisionNumber", (string?)null);
            set => SetValue(CompoundProperty.SummaryInformationFormatId, "RevisionNumber", value);
        }

        /// <summary>
        /// Gets or sets the title.
        /// </summary>
        /// <value>The title.</value>
        public string? Title
        {
            get => GetValue(CompoundProperty.SummaryInformationFormatId, "Title", (string?)null);
            set => SetValue(CompoundProperty.SummaryInformationFormatId, "Title", value);
        }

        /// <summary>
        /// Gets or sets the subject.
        /// </summary>
        /// <value>The subject.</value>
        public string? Subject
        {
            get => GetValue(CompoundProperty.SummaryInformationFormatId, "Subject", (string?)null);
            set => SetValue(CompoundProperty.SummaryInformationFormatId, "Subject", value);
        }

        /// <summary>
        /// Gets or sets the name of the application.
        /// </summary>
        /// <value>The name of the application.</value>
        public string? ApplicationName
        {
            get => GetValue(CompoundProperty.SummaryInformationFormatId, "ApplicationName", (string?)null);
            set => SetValue(CompoundProperty.SummaryInformationFormatId, "ApplicationName", value);
        }

        /// <summary>
        /// Gets or sets the comments.
        /// </summary>
        /// <value>The comments.</value>
        public string? Comments
        {
            get => GetValue(CompoundProperty.SummaryInformationFormatId, "Comments", (string?)null);
            set => SetValue(CompoundProperty.SummaryInformationFormatId, "Comments", value);
        }

        /// <summary>
        /// Gets or sets the template.
        /// </summary>
        /// <value>The template.</value>
        public string? Template
        {
            get => GetValue(CompoundProperty.SummaryInformationFormatId, "Template", (string?)null);
            set => SetValue(CompoundProperty.SummaryInformationFormatId, "Template", value);
        }

        /// <summary>
        /// Gets or sets the security.
        /// </summary>
        /// <value>The security.</value>
        public int? Security
        {
            get => GetValue(CompoundProperty.SummaryInformationFormatId, "Security", (int?)null);
            set => SetValue(CompoundProperty.SummaryInformationFormatId, "Security", value);
        }

        /// <summary>
        /// Gets or sets the characters count.
        /// </summary>
        /// <value>The characters.</value>
        public int? Characters
        {
            get => GetValue(CompoundProperty.SummaryInformationFormatId, "Characters", (int?)null);
            set => SetValue(CompoundProperty.SummaryInformationFormatId, "Characters", value);
        }

        /// <summary>
        /// Gets or sets the pages count.
        /// </summary>
        /// <value>The pages count.</value>
        public int? Pages
        {
            get => GetValue(CompoundProperty.SummaryInformationFormatId, "Pages", (int?)null);
            set => SetValue(CompoundProperty.SummaryInformationFormatId, "Pages", value);
        }

        /// <summary>
        /// Gets or sets the words count.
        /// </summary>
        /// <value>The words count.</value>
        public int? Words
        {
            get => GetValue(CompoundProperty.SummaryInformationFormatId, "Words", (int?)null);
            set => SetValue(CompoundProperty.SummaryInformationFormatId, "Words", value);
        }

        /// <summary>
        /// Gets or sets the create date.
        /// </summary>
        /// <value>The create date.</value>
        public DateTime? CreateDate
        {
            get => GetValue(CompoundProperty.SummaryInformationFormatId, "CreateDate", (DateTime?)null);
            set => SetValue(CompoundProperty.SummaryInformationFormatId, "CreateDate", value);
        }

        /// <summary>
        /// Gets or sets the last printed date.
        /// </summary>
        /// <value>The last printed.</value>
        public DateTime? LastPrintedDate
        {
            get => GetValue(CompoundProperty.SummaryInformationFormatId, "LastPrintedDate", (DateTime?)null);
            set => SetValue(CompoundProperty.SummaryInformationFormatId, "LastPrintedDate", value);
        }

        /// <summary>
        /// Gets or sets the last saved date.
        /// </summary>
        /// <value>The last saved date.</value>
        public DateTime? LastSavedDate
        {
            get => GetValue(CompoundProperty.SummaryInformationFormatId, "LastSavedDate", (DateTime?)null);
            set => SetValue(CompoundProperty.SummaryInformationFormatId, "LastSavedDate", value);
        }

        /// <summary>
        /// Gets or sets the total editing time.
        /// </summary>
        /// <value>The total editing time.</value>
        public DateTime? TotalEditingTime
        {
            get => GetValue(CompoundProperty.SummaryInformationFormatId, "TotalEditingTime", (DateTime?)null);
            set => SetValue(CompoundProperty.SummaryInformationFormatId, "TotalEditingTime", value);
        }
    }
}
