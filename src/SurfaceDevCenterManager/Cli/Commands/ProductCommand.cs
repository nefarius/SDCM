/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using SurfaceDevCenterManager.Handlers;

namespace SurfaceDevCenterManager.Cli.Commands;

internal static class ProductCommand
{
    public static Command Build(ServiceProviderAccessor accessor)
    {
        Option<string> input = Opt.Str("--input", "Path to a JSON file with the NewProduct payload", true);
        Command create = new("create", "Create a new product");
        create.Options.Add(input);
        create.SetHandlerAction(
            accessor,
            (pr, global) => new ProductCreateInput(pr.Required(input), global),
            (sp, i, ct) => sp.GetRequiredService<ProductCreateHandler>().RunAsync(i, ct));

        Option<string?> listProductId = Opt.OptionalStr("--product-id",
            "Deprecated: still returns a one-element array. Prefer 'product get'.");
        Command list = new("list", "List every product");
        list.Options.Add(listProductId);
        list.SetHandlerAction(
            accessor,
            (pr, global) => new ProductListInput(pr.GetValue(listProductId), global),
            (sp, i, ct) => sp.GetRequiredService<ProductListHandler>().RunAsync(i, ct));

        Option<string> getProductId = Opt.Str("--product-id", "Product id to fetch", true);
        Command get = new("get", "Get a single product by id");
        get.Options.Add(getProductId);
        get.SetHandlerAction(
            accessor,
            (pr, global) => new ProductGetInput(pr.Required(getProductId), global),
            (sp, i, ct) => sp.GetRequiredService<ProductGetHandler>().RunAsync(i, ct));

        Command product = new("product", "Manage Hardware Dev Center products");
        product.Subcommands.Add(create);
        product.Subcommands.Add(list);
        product.Subcommands.Add(get);
        return product;
    }
}
