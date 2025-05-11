  interface Props {
    children?: React.ReactNode;
  }

  const PageContent = ({ children }: Props) => {
    return (
  <main className="flex flex-col gap-5 py-8 px-4 sm:px-6 lg:px-8 w-full max-w-none">
    {children}
  </main>
    );
  };

  export default PageContent;
