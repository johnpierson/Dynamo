using System;
using System.Collections.Generic;
using System.Linq;
using Dynamo.Engine;
using Dynamo.Models;
using System.Text;
using Dynamo.Graph;
using Dynamo.Graph.Nodes;
using Dynamo.Graph.Nodes.ZeroTouch;

namespace Dynamo.Search.SearchElements
{
    /// <summary>
    ///     Search element for a Zero Touch node (DSFunction / DSVarArgFunction)
    /// </summary>
    public class ZeroTouchSearchElement : NodeSearchElement
    {
        private readonly FunctionDescriptor functionDescriptor;
        private readonly string fullname;

        /// <summary>
        /// The name that is used during node creation
        /// </summary>
        public override string CreationName { get { return functionDescriptor != null ? functionDescriptor.MangledName : this.Name; } }

        /// <summary>
        ///     The full name of entry which consists of assembly name and qualified name for function descriptor.
        /// </summary>
        public override string FullName { get { return fullname; } }
        /// <summary>
        /// Retrieve underlying Function Descriptor.
        /// </summary>
        internal FunctionDescriptor Descriptor => functionDescriptor;

        internal override bool IsExperimental => functionDescriptor.IsExperimental;

        /// <summary>
        /// Initializes a new instance of the <see cref="ZeroTouchSearchElement"/> class 
        /// with the DesignScript function description
        /// </summary>
        /// <param name="functionDescriptor"><see cref="FunctionDescriptor"/> object
        /// describing DesignScript function.</param>
        public ZeroTouchSearchElement(FunctionDescriptor functionDescriptor)
        {
            this.functionDescriptor = functionDescriptor;

            Name = functionDescriptor.UserFriendlyName;
            
            if (functionDescriptor.IsOverloaded)
            {
                var parameters = new StringBuilder();
                parameters.Append("(");
                parameters.Append(String.Join(", ", functionDescriptor.Parameters.Select(x => x.Name)));
                parameters.Append(")");

                Parameters = parameters.ToString();
            }
            
            FullCategoryName = functionDescriptor.Category;
            Description = functionDescriptor.Description;
            Assembly = functionDescriptor.Assembly;

            //Create full name including assembly name
            fullname = string.Format("{0}.{1}", System.IO.Path.GetFileNameWithoutExtension(Assembly), functionDescriptor.QualifiedName);
            ElementType = ElementTypes.ZeroTouch;

            if (functionDescriptor.IsBuiltIn)
                ElementType |= ElementTypes.BuiltIn;

            if (functionDescriptor.IsPackageMember)
                ElementType |= ElementTypes.Packaged;

            inputParameters = new List<Tuple<string, string>>(functionDescriptor.InputParameters);
            outputParameters = new List<string>() { functionDescriptor.ReturnType.ToShortString() };

            foreach (var tag in functionDescriptor.GetSearchTags())
                SearchKeywords.Add(tag);

            var weights = functionDescriptor.GetSearchTagWeights();
            foreach (var weight in weights)
            {
                // Search tag weight can't be more then 1.
                if (weight <= 1)
                    keywordWeights.Add(weight);
            }

            int weightsCount = weights.Count();
            // If there weren't added weights for search tags, then add default value - 0.5
            if (weightsCount != SearchKeywords.Count)
            {
                int numberOfLackingWeights = SearchKeywords.Count - weightsCount;

                // Number of lacking weights should be more than 0.
                // It can be less then 0 only if there was some mistake in xml file.
                if (numberOfLackingWeights > 0)
                {
                    for (int i = 0; i < numberOfLackingWeights; i++)
                    {
                        keywordWeights.Add(0.5);
                    }
                }

            }

            iconName = Graph.Nodes.Utilities.GetFunctionDescriptorIconName(functionDescriptor);
        }

        protected override NodeModel ConstructNewNodeModel()
        {
            if (functionDescriptor.IsVarArg)
                return new DSVarArgFunction(functionDescriptor);
            return new DSFunction(functionDescriptor);
        }
    }
}
