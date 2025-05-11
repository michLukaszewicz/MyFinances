interface Props {
  children?: React.ReactNode;
}

const PageContent = ({ children }: Props) => {
  return (
    <main className="py-8 max-w-7x1 mx-auto px-4 sm:px-6 lg:px-8 w-full">
      {children}
    </main>
  );
};

export default PageContent;
