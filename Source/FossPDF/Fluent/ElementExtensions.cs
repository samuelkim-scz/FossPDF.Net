using System;
using FossPDF.Drawing.Exceptions;
using FossPDF.Elements;
using FossPDF.Helpers;
using FossPDF.Infrastructure;

namespace FossPDF.Fluent
{
    public static class ElementExtensions
    {
        static ElementExtensions()
        {
            NativeDependencyCompatibilityChecker.Test();
        }
        
        internal static Container Create(Action<IContainer> factory)
        {
            var container = new Container();
            factory(container);
            return container;
        }

        internal static T Element<T>(this IContainer element, T child) where T : IElement?
        {
            if (element == null)
                throw new ArgumentNullException(nameof(element));

            if (element.Child != null && element.Child is Empty == false)
            {
                var message = "You should not assign multiple child elements to a single-child container. " +
                              "This may happen when a container variable is used outside of its scope/closure OR the container is used in multiple fluent API chains OR the container is used incorrectly in a loop. " +
                              "This exception is thrown to help you detect that some part of the code is overriding fragments of the document layout with a new content - essentially destroying existing content.";

                throw new DocumentComposeException(message);
            }

            if (element != child as Element)
                element.Child = child as Element;
            
            return child;
        }
        
        public static void Element<TParent>(this TParent parent, Action<IContainer> handler) where TParent : IContainer
        {
            handler(parent.Container());
        }
        
        public static IContainer Element<TParent>(this TParent parent, Func<IContainer, IContainer> handler) where TParent : IContainer
        {
            return handler(parent.Container()).Container();
        }
        
        public static IContainer AspectRatio(this IContainer element, float ratio, AspectRatioOption option = AspectRatioOption.FitWidth)
        {
            return element.Element(new AspectRatio
            {
                Ratio = ratio,
                Option = option
            });
        }

        public static IContainer Background(this IContainer element, string color)
        {
            ColorValidator.Validate(color);

            // Reuse an existing decoration so corner radii and the border share one rectangle.
            var background = element as RectangleDecoration ?? element.Element(new Background());
            background.FillColor = color;

            return background;
        }

        public static void Placeholder(this IContainer element, string? text = null)
        {
            element.Component(new Placeholder
            {
                Text = text ?? string.Empty
            });
        }

        public static IContainer ShowOnce(this IContainer element)
        {
            return element.Element(new ShowOnce());
        }

        public static IContainer SkipOnce(this IContainer element)
        {
            return element.Element(new SkipOnce());
        }

        public static IContainer ShowEntire(this IContainer element)
        {
            return element.Element(new ShowEntire());
        }

        public static IContainer EnsureSpace(this IContainer element, float minHeight = Elements.EnsureSpace.DefaultMinHeight)
        {
            return element.Element(new EnsureSpace
            {
                MinHeight = minHeight
            });
        }

        /// <summary>
        /// Attempts to keep the content together on the first page where it appears. If the content
        /// does not fit entirely, it is moved to the next page. Content longer than one page
        /// continues normally on subsequent pages.
        /// </summary>
        public static IContainer PreventPageBreak(this IContainer element)
        {
            return element.Element(new PreventPageBreak());
        }

        /// <summary>
        /// Repeats the content on every page where the surrounding layout is still being rendered.
        /// Use it for labels or keys that should remain visible while adjacent content continues.
        /// </summary>
        public static IContainer Repeat(this IContainer element)
        {
            return element.Element(new RepeatContent());
        }

        public static void PageBreak(this IContainer element)
        {
            element.Element(new PageBreak());
        }
        
        public static IContainer Container(this IContainer element)
        {
            return element.Element(new Container());
        }
        
        [Obsolete("This element has been renamed since version 2022.3. Please use the Hyperlink method.")]
        public static IContainer ExternalLink(this IContainer element, string url)
        {
            return element.Hyperlink(url);
        }
        
        public static IContainer Hyperlink(this IContainer element, string url)
        {
            return element.Element(new Hyperlink
            {
                Url = url
            });
        }
        
        [Obsolete("This element has been renamed since version 2022.3. Please use the Section method.")]
        public static IContainer Location(this IContainer element, string locationName)
        {
            return element.Section(locationName);
        }
        
        public static IContainer Section(this IContainer element, string sectionName)
        {
            return element.Element(new Section
            {
                LocationName = sectionName
            });
        }
        
        [Obsolete("This element has been renamed since version 2022.3. Please use the SectionLink method.")]
        public static IContainer InternalLink(this IContainer element, string locationName)
        {
            return element.SectionLink(locationName);
        }
        
        public static IContainer SectionLink(this IContainer element, string sectionName)
        {
            return element.Element(new SectionLink
            {
                SectionName = sectionName
            });
        }
        
        public static IContainer ShowIf(this IContainer element, bool condition)
        {
            return condition ? element : new Container();
        }

        /// <summary>
        /// Conditionally draws the content based on the current page context.
        /// The total page count is <c>null</c> during the initial layout pass, so predicates
        /// must handle that case. Changing content size between passes can produce unstable layouts.
        /// </summary>
        public static IContainer ShowIf(this IContainer element, Predicate<ShowIfContext> predicate)
        {
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));

            return element.Element(new ShowIf
            {
                VisibilityPredicate = predicate
            });
        }
        
        /// <summary>
        /// Records the element's page, position, size and canvas transform during layout. All
        /// occurrences are available through <see cref="FossPDF.Infrastructure.PageElementLocation"/>
        /// on the generated <see cref="Document"/> or from a dynamic component.
        /// </summary>
        public static IContainer CaptureContentPosition(this IContainer element, string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("The content position identifier cannot be null or whitespace.", nameof(id));

            return element.Element(new ElementPositionLocator
            {
                Id = id
            });
        }

        public static void Canvas(this IContainer element, DrawOnCanvas handler)
        {
            element.Element(new Canvas
            {
                Handler = handler
            });
        }
        
        [Obsolete("This element has been renamed since version 2022.1. Please use the MinimalBox method.")]
        public static IContainer Box(this IContainer element)
        {
            return element.Element(new MinimalBox());
        }
        
        public static IContainer MinimalBox(this IContainer element)
        {
            return element.Element(new MinimalBox());
        }
        
        public static IContainer Unconstrained(this IContainer element)
        {
            return element.Element(new Unconstrained());
        }

        /// <summary>
        /// Places the element at the given stacking depth. Higher values render above lower values.
        /// </summary>
        public static IContainer ZIndex(this IContainer element, int indexValue)
        {
            return element.Element(new ZIndex
            {
                Depth = indexValue
            });
        }
        
        public static IContainer DefaultTextStyle(this IContainer element, TextStyle textStyle)
        {
            return element.Element(new DefaultTextStyle
            {
                TextStyle = textStyle
            });
        }
        
        public static IContainer DefaultTextStyle(this IContainer element, Func<TextStyle, TextStyle> handler)
        {
            return element.Element(new DefaultTextStyle
            {
                TextStyle = handler(TextStyle.Default)
            });
        }
        
        public static IContainer StopPaging(this IContainer element)
        {
            return element.Element(new StopPaging());
        }
        
        public static IContainer ScaleToFit(this IContainer element)
        {
            return element.Element(new ScaleToFit());
        }
    }
}
